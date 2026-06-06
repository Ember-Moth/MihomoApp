using Android.App;
using Android.Content;
using Android.OS;
using Android.Util;

namespace Aureline.Android.Services;

internal sealed class AndroidCoreIpcClient : IDisposable
{
    private const string Tag = nameof(AndroidCoreIpcClient);

    private readonly Activity activity;
    private readonly object gate = new();
    private readonly Messenger responseMessenger;
    private readonly Dictionary<long, TaskCompletionSource<string>> pendingRequests = new();
    private long nextRequestId;
    private Messenger? remoteMessenger;
    private CoreServiceConnection? connection;
    private TaskCompletionSource<Messenger>? connectionCompletion;
    private bool disposed;

    public AndroidCoreIpcClient(Activity activity)
    {
        this.activity = activity;
        responseMessenger = new Messenger(new ResponseHandler(this));
    }

    public async Task<string> SendAsync(
        string command,
        string payload = "",
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        var messenger = await EnsureConnectedAsync(cancellationToken);
        var requestId = Interlocked.Increment(ref nextRequestId);
        var completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        lock (gate)
        {
            pendingRequests[requestId] = completion;
        }

        using var cancellation = cancellationToken.Register(() =>
        {
            TaskCompletionSource<string>? removed = null;
            lock (gate)
            {
                if (pendingRequests.Remove(requestId, out var pending))
                {
                    removed = pending;
                }
            }

            removed?.TrySetCanceled(cancellationToken);
        });

        var message = Message.Obtain(null, AndroidIpcWire.MessageRequest) ??
            throw new InvalidOperationException("Failed to allocate IPC request message");
        var data = new Bundle();
        data.PutLong(AndroidIpcWire.ExtraRequestId, requestId);
        data.PutString(AndroidIpcWire.ExtraCommand, command);
        data.PutString(AndroidIpcWire.ExtraPayload, payload);
        message.Data = data;
        message.ReplyTo = responseMessenger;

        try
        {
            messenger.Send(message);
            return await completion.Task;
        }
        catch
        {
            RemovePendingRequest(requestId)?.TrySetException(new InvalidOperationException("Core IPC request failed"));
            ClearConnection();
            throw;
        }
        finally
        {
            message.Dispose();
            data.Dispose();
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;

        lock (gate)
        {
            foreach (var pending in pendingRequests.Values)
            {
                pending.TrySetCanceled();
            }

            pendingRequests.Clear();
            connectionCompletion?.TrySetCanceled();
            connectionCompletion = null;
        }

        if (connection != null)
        {
            try
            {
                activity.UnbindService(connection);
            }
            catch (Exception ex)
            {
                Log.Debug(Tag, $"Failed to unbind core service: {ex.Message}");
            }
        }

        connection?.Dispose();
        connection = null;
        remoteMessenger?.Dispose();
        remoteMessenger = null;
        responseMessenger.Dispose();
    }

    private Task<Messenger> EnsureConnectedAsync(CancellationToken cancellationToken)
    {
        lock (gate)
        {
            if (remoteMessenger != null)
            {
                return Task.FromResult(remoteMessenger);
            }

            if (connectionCompletion == null)
            {
                connectionCompletion = new TaskCompletionSource<Messenger>(TaskCreationOptions.RunContinuationsAsynchronously);
                connection = new CoreServiceConnection(this);

                var intent = CoreServiceIntent();
                activity.StartService(intent);
                if (!activity.BindService(intent, connection, Bind.AutoCreate))
                {
                    connectionCompletion.TrySetException(new InvalidOperationException("无法绑定核心服务"));
                }
            }

            return connectionCompletion.Task.WaitAsync(cancellationToken);
        }
    }

    private Intent CoreServiceIntent()
    {
        var intent = new Intent(activity, typeof(AurelineCoreService));
        intent.SetAction(AndroidIpcWire.ActionBindCore);
        return intent;
    }

    private void HandleServiceConnected(IBinder binder)
    {
        var messenger = new Messenger(binder);
        lock (gate)
        {
            remoteMessenger = messenger;
            connectionCompletion?.TrySetResult(messenger);
        }
    }

    private void HandleServiceDisconnected()
    {
        Log.Warn(Tag, "Core service disconnected");
        ClearConnection();
    }

    private void ClearConnection()
    {
        List<TaskCompletionSource<string>> pending;
        lock (gate)
        {
            remoteMessenger?.Dispose();
            remoteMessenger = null;
            connectionCompletion = null;
            pending = pendingRequests.Values.ToList();
            pendingRequests.Clear();
        }

        foreach (var request in pending)
        {
            request.TrySetException(new InvalidOperationException("核心服务连接已断开"));
        }
    }

    private TaskCompletionSource<string>? RemovePendingRequest(long requestId)
    {
        lock (gate)
        {
            if (pendingRequests.Remove(requestId, out var pending))
            {
                return pending;
            }
        }

        return null;
    }

    private void HandleResponse(Message message)
    {
        if (message.What != AndroidIpcWire.MessageResponse)
        {
            return;
        }

        var data = message.Data;
        if (data == null)
        {
            return;
        }

        var requestId = data.GetLong(AndroidIpcWire.ExtraRequestId);
        var success = data.GetBoolean(AndroidIpcWire.ExtraSuccess);
        var payload = data.GetString(AndroidIpcWire.ExtraPayload) ?? string.Empty;
        var error = data.GetString(AndroidIpcWire.ExtraError) ?? string.Empty;
        var pending = RemovePendingRequest(requestId);
        if (pending == null)
        {
            return;
        }

        if (success)
        {
            pending.TrySetResult(payload);
            return;
        }

        pending.TrySetException(new InvalidOperationException(
            string.IsNullOrWhiteSpace(error) ? "核心服务命令执行失败" : error));
    }

    private sealed class CoreServiceConnection(AndroidCoreIpcClient client) : Java.Lang.Object, IServiceConnection
    {
        public void OnServiceConnected(ComponentName? name, IBinder? service)
        {
            if (service == null)
            {
                client.ClearConnection();
                return;
            }

            client.HandleServiceConnected(service);
        }

        public void OnServiceDisconnected(ComponentName? name)
        {
            client.HandleServiceDisconnected();
        }
    }

    private sealed class ResponseHandler(AndroidCoreIpcClient client) : Handler(Looper.MainLooper!)
    {
        public override void HandleMessage(Message msg)
        {
            client.HandleResponse(msg);
        }
    }
}
