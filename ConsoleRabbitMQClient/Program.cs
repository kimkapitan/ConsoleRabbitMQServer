using System;
using System.Collections.Concurrent;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

public class RpcClient
{
    private readonly IConnection connection;
    private readonly IModel channel;
    private readonly string replyQueueName;
    private readonly EventingBasicConsumer consumer;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<string>> callbackMapper =
        new ConcurrentDictionary<string, TaskCompletionSource<string>>();

    public RpcClient()
    {
        var factory = new ConnectionFactory() { HostName = "localhost" };

        connection = factory.CreateConnection();
        channel = connection.CreateModel();
        replyQueueName = channel.QueueDeclare().QueueName;
        consumer = new EventingBasicConsumer(channel);



        consumer.Received += (model, ea) =>
        {
            if (!callbackMapper.TryRemove(ea.BasicProperties.CorrelationId, out TaskCompletionSource<string> tcs))
                return;
            var body = ea.Body;
            var response = Encoding.UTF8.GetString(body);
            tcs.TrySetResult(response);
        };
    }

    public async Task<string> CallAsync(string message, CancellationToken cancellationToken = default)
    {
        var props = channel.CreateBasicProperties();
        var correlationId = Guid.NewGuid().ToString();
        props.CorrelationId = correlationId;
        props.ReplyTo = replyQueueName;

        var tcs = new TaskCompletionSource<string>();
        callbackMapper.TryAdd(correlationId, tcs);

        var messageBytes = Encoding.UTF8.GetBytes(message);
        channel.BasicPublish(
            exchange: "Test",
            routingKey: "1", //клиент отправляет только на route = "1"
            basicProperties: props,
            body: messageBytes);

        channel.BasicConsume(
            consumer: consumer,
            queue: replyQueueName,
            autoAck: true);

        cancellationToken.Register(() => callbackMapper.TryRemove(correlationId, out var tmp));
        return await tcs.Task;
    }

    public void Close()
    {
        connection.Close();
    }
}

public class Rpc
{
    public static async Task Main(string[] args)
    {
        var val = args.Length < 1 ? "2" : args[0];

        var rpcClient = new RpcClient();

        Console.WriteLine($" [x] Requesting {val}");
        var response = await rpcClient.CallAsync(val);

        Console.WriteLine(" [.] Got '{0}'", response);
        rpcClient.Close();
        Console.ReadLine();
    }
}
