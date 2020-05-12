using System;
using System.Runtime.CompilerServices;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Discord;
using Discord.WebSocket;
using Discord.Commands;
using Discord.Rest;
using System.Threading;
using System.Net;
using Newtonsoft.Json;
using Discord.Addons.Interactive;

namespace MopsBot
{
    public class Program
    {
        public static void Main(string[] args)
        {
            Task.Run(() => BuildWebHost(args).Run());
            new Program().Start().GetAwaiter().GetResult();
        }

        public static Dictionary<string, string> Config;

        private async Task Start()
        {

            using (StreamReader sr = new StreamReader(new FileStream("mopsdata//Config.json", FileMode.Open)))
                Config = JsonConvert.DeserializeObject<Dictionary<string, string>>(sr.ReadToEnd());

            StaticBase.UpdateStatusAsync();
            StaticBase.initTracking();

            await Task.Delay(-1);
        }

        public static async Task MopsLog(LogMessage msg, [CallerMemberName] string callerName = "", [CallerFilePath] string callerPath = "", [CallerLineNumber] int callerLine = 0)
        {
            string message = $"\n[{msg.Severity}] at {DateTime.Now}\nsource: {Path.GetFileNameWithoutExtension(callerPath)}.{callerName}, line: {callerLine}\nmessage: {msg.Message}";
            if (msg.Exception != null && !msg.Exception.Message.Contains("The SSL connection could not be established"))
            {
                message += $"\nException: {msg.Exception?.Message ?? ""}\nStacktrace: {msg.Exception?.StackTrace ?? ""}";
            }

            Console.WriteLine(message);
        }

        public static IWebHost BuildWebHost(string[] args) =>
            WebHost.CreateDefaultBuilder(args)
                .UseUrls("http://0.0.0.0:5000/")
                .ConfigureServices(x => x.AddCors(options => options.AddPolicy("AllowAll",
                    builder =>
                    {
                        builder.AllowAnyOrigin()
                               .AllowAnyHeader()
                               .AllowAnyMethod()
                               .AllowCredentials();
                    })))
                .UseKestrel()
                .UseContentRoot(Directory.GetCurrentDirectory())
                .UseIISIntegration()
                .UseStartup<Startup>()
                .Build();
    }
}
