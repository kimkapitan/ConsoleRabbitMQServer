using System;
using System.Security.Cryptography.X509Certificates;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Text;

class RPCServer
{
    public static void Main(string[] args)
    {
        var route = args.Length < 1 ? "1" : args[0]; // устанавливаю route

        var factory = new ConnectionFactory() { HostName = "localhost" };
        using var connection = factory.CreateConnection();
        using var channel = connection.CreateModel();
        channel.QueueDeclare(queue: "rpc_queue", durable: true, exclusive: false, autoDelete: false, arguments: null);

        channel.ExchangeDeclare("Test", ExchangeType.Direct);
        channel.QueueBind("rpc_queue", "Test", route);

        channel.BasicQos(0, 1, false);
        var consumer = new EventingBasicConsumer(channel);
        channel.BasicConsume(queue: "rpc_queue",
          autoAck: false, consumer: consumer);
        Console.WriteLine(" [x] Awaiting RPC requests");

        consumer.Received += (model, ea) =>
        {
            string response = null;

            var body = ea.Body;
            var props = ea.BasicProperties;
            var replyProps = channel.CreateBasicProperties();
            replyProps.CorrelationId = props.CorrelationId;

            try
            {
                var message = Encoding.UTF8.GetString(body);
                int n = int.Parse(message);
                Console.WriteLine(" [.] req({0})", message);
                response = (n + 1).ToString(); // увеличиваю значение на еденицу и отправляю обратно
                Console.WriteLine(" [.] resp({0})", response);
            }
            catch (Exception e)
            {
                Console.WriteLine(" [.] " + e.Message);
                response = "";
            }
            finally
            {
                var responseBytes = Encoding.UTF8.GetBytes(response);
                channel.BasicPublish(exchange: "", routingKey: props.ReplyTo, basicProperties: replyProps, body: responseBytes); //станадартная отправка обратно
                channel.BasicAck(deliveryTag: ea.DeliveryTag,
                  multiple: false);
            }
        };

        Console.WriteLine(" Press [enter] to exit.");
        Console.ReadLine();
    }
}