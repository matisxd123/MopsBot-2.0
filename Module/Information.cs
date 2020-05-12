using System;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Newtonsoft.Json;
using System.Net.NetworkInformation;
using System.Net.Http;
using System.Xml.Serialization;

namespace MopsBot.Module
{
    public class Information : ModuleBase<ShardedCommandContext>
    {
        public static int FailedRequests = 0, SucceededRequests = 0, FailedRequestsTotal = 0, SucceededRequestsTotal = 0;

        public static async Task<string> PostURLAsync(string URL, string body = "", params KeyValuePair<string, string>[] headers)
        {
            if (FailedRequests >= 10 && SucceededRequests / FailedRequests < 1)
            {
                await Program.MopsLog(new LogMessage(LogSeverity.Warning, "HttpRequests", $"More Failed requests {FailedRequests} than succeeded ones {SucceededRequests}. Waiting"));
                return "";
            }

            HttpRequestMessage test = new HttpRequestMessage(HttpMethod.Post, URL);
            test.Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
            foreach (var header in headers)
                test.Headers.TryAddWithoutValidation(header.Key, header.Value);

            using (var response = await StaticBase.HttpClient.SendAsync(test))
            {
                try
                {
                    string value = await response.Content.ReadAsStringAsync();
                    SucceededRequests++;
                    SucceededRequestsTotal++;
                    return value;
                }
                catch (Exception e)
                {
                    FailedRequests++;
                    FailedRequestsTotal++;
                    await Program.MopsLog(new LogMessage(LogSeverity.Error, "", $"error for sending post request to {URL}", e.GetBaseException()));
                    throw e;
                }
            }
        }

        public static async Task<string> GetURLAsync(string URL, params KeyValuePair<string, string>[] headers)
        {
            if (FailedRequests >= 10 && SucceededRequests / FailedRequests < 1)
            {
                await Program.MopsLog(new LogMessage(LogSeverity.Warning, "HttpRequests", $"More Failed requests {FailedRequests} than succeeded ones {SucceededRequests}. Waiting"));
                return "";
            }

            try
            {
                using (var request = new HttpRequestMessage(HttpMethod.Get, URL))
                {
                    foreach (var kvp in headers)
                        request.Headers.TryAddWithoutValidation(kvp.Key, kvp.Value);
                    using (var response = await StaticBase.HttpClient.SendAsync(request))
                    {
                        using (var content = response.Content)
                        {
                            string value = "";
                            if ((content?.Headers?.ContentType?.CharSet?.ToLower().Contains("utf8") ?? false) || (content?.Headers?.ContentType?.CharSet?.ToLower().Contains("utf-8") ?? false))
                                value = System.Text.Encoding.UTF8.GetString(await content.ReadAsByteArrayAsync());
                            else
                                value = await content.ReadAsStringAsync();

                            SucceededRequests++;
                            SucceededRequestsTotal++;
                            return value;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                FailedRequests++;
                FailedRequestsTotal++;
                if (!e.GetBaseException().Message.Contains("the remote party has closed the transport stream") && !e.GetBaseException().Message.Contains("The server returned an invalid or unrecognized response"))
                    await Program.MopsLog(new LogMessage(LogSeverity.Error, "", $"error for sending request to {URL}", e.GetBaseException()));
                else if (e.GetBaseException().Message.Contains("the remote party has closed the transport stream"))
                    await Program.MopsLog(new LogMessage(LogSeverity.Warning, "", $"Remote party closed the transport stream: {URL}."));
                else
                    await Program.MopsLog(new LogMessage(LogSeverity.Debug, "", $"Osu API messed up again: {URL}"));
                throw e;
            }
        }
    }
}

