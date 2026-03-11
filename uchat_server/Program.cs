namespace uchat_server;

class Program
{
    static async Task Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("Usage: uchat_server <port>");
            Console.WriteLine("Example: uchat_server 8080");
            return;
        }

        if (!int.TryParse(args[0], out var port) || port < 1 || port > 65535)
        {
            Console.WriteLine("Error: Invalid port number. Port must be between 1 and 65535.");
            return;
        }

        var server = new Server(port);
        
        _ = Task.Run(async () =>
        {
            while (true)
            {
                try
                {
                    await server.CheckAndSendScheduledMessagesAsync();
                    await Task.Delay(TimeSpan.FromSeconds(10)); 
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error checking scheduled messages: {ex.Message}");
                    await Task.Delay(TimeSpan.FromSeconds(30)); 
                }
            }
        });
        
        try
        {
            await server.StartAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Server error: {ex.Message}");
        }
        finally
        {
            server.Stop();
        }
    }
}