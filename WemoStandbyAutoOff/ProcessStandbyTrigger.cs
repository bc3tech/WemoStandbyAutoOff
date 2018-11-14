using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace WemoStandbyAutoOff
{
    public static class ProcessStandbyTrigger
    {
        private const string STANDBY_EVENT_NAME = @"StandbyEvent";
        private static readonly HttpClient _client = new HttpClient();

        [FunctionName("StandbyAutoOff")]
        public static async Task<IActionResult> RunAsync(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = null)] HttpRequest req,
            ILogger log,
            [OrchestrationClient]DurableOrchestrationClient durableClient)
        {
            var bodyString = new StreamReader(req.Body).ReadToEnd();
            log.LogInformation($@"*** Request received: {bodyString}");
            var body = JToken.Parse(bodyString);

            var wemoId = body[@"wemoId"]?.Value<string>();
            if (string.IsNullOrWhiteSpace(wemoId))
            {
                return new BadRequestObjectResult(new
                {
                    error = @"'wemoId' required"
                });
            }

            var standbyEntered = body[@"standbyEntered"]?.Value<bool>();
            if (standbyEntered == null)
            {
                return new BadRequestObjectResult(new
                {
                    error = @"'standbyEntered' required. 'true' if standby was entered on this Wemo device, 'false' otherwise"
                });
            }
            else if (standbyEntered == true)
            {
                var timeoutMinutes = body[@"timeoutDurationMinutes"]?.Value<int>();
                if (timeoutMinutes == null)
                {
                    return new BadRequestObjectResult(new
                    {
                        error = @"If 'standbyEntered' = true, 'timeoutDurationMinutes' is required. Set to the number of seconds to wait after going in to Standby before issuing an HTTP POST to 'callbackUrl'."
                    });
                }

                var callbackUrl = body[@"callbackUrl"]?.Value<string>();
                var instanceStatus = await durableClient.GetStatusAsync(wemoId);
                if (null == instanceStatus)
                {
                    if (string.IsNullOrWhiteSpace(callbackUrl))
                    {
                        return new BadRequestObjectResult(new
                        {
                            error = @"If 'standbyEntered' = true, 'callbackUrl' is required"
                        });
                    }
                    await durableClient.StartNewAsync(nameof(StartActor), wemoId, new
                    {
                        callbackUrl,
                        timeoutMinutes
                    });

                    while ((instanceStatus = await durableClient.GetStatusAsync(wemoId))?.RuntimeStatus == OrchestrationRuntimeStatus.Pending)
                    {
                        log.LogInformation(@"*** Waiting for orchestrator to start...");
                        await Task.Delay(500);
                    }
                }
            }

            if ((await durableClient.GetStatusAsync(wemoId))?.RuntimeStatus == OrchestrationRuntimeStatus.Running)
            {
                await durableClient.RaiseEventAsync(wemoId, STANDBY_EVENT_NAME, new { standbyEntered });
            }

            return new AcceptedResult();
        }

        [FunctionName(nameof(StartActor))]
        public static async Task StartActor([OrchestrationTrigger]DurableOrchestrationContext context, ILogger log)
        {
            var input = context.GetInput<JObject>();
            var callbackUrl = input.Value<string>(@"callbackUrl");
            var timeout = TimeSpan.FromMinutes(input[@"timeoutMinutes"].Value<int>());

            if (!context.IsReplaying)
            {
                log.LogInformation($@"*** Waiting for first toggle event for {context.InstanceId} ...");
            }

            do
            {
                var inStandby = false;
                var signal = await context.WaitForExternalEvent<JObject>(STANDBY_EVENT_NAME);
                if (!context.IsReplaying)
                {
                    log.LogInformation($@"*** Event Received: {signal.ToString()}");
                }

                if (signal?.Value<bool>(@"standbyEntered") == true)
                {
                    do
                    {
                        if (!context.IsReplaying)
                        {
                            log.LogInformation($@"*** Waiting for next standby event...");
                        }

                        signal = null;
                        try
                        {
                            signal = await context.WaitForExternalEvent<JObject>(STANDBY_EVENT_NAME, timeout);
                        }
                        catch (TimeoutException)
                        {
                            if (!context.IsReplaying)
                            {
                                log.LogInformation(@"*** Timeout hit ***");
                            }

                            break;
                        }

                        if (!context.IsReplaying)
                        {
                            log.LogInformation($@"Got it: {signal.ToString()}");
                        }
                    } while (signal.Value<bool>(@"standbyEntered"));

                    inStandby = (signal == null || signal.Value<bool>(@"standbyEntered"));
                    if (!context.IsReplaying)
                    {
                        log.LogInformation($@"*** In standby? {inStandby}");
                    }
                }

                if (inStandby)
                {
                    await context.CallActivityAsync(nameof(TriggerIftttUrl), new { callbackUrl });
                }
                else
                {
                    if (!context.IsReplaying)
                    {
                        log.LogInformation(@"*** Device turned on. Re-waiting for device to go in to standby...");
                    }
                }
            } while (true);
        }

        [FunctionName(nameof(TriggerIftttUrl))]
        public static async Task TriggerIftttUrl([ActivityTrigger]DurableActivityContext context, ILogger log)
        {
            var callbackUrl = context.GetInput<JObject>().Value<string>(@"callbackUrl");

            log.LogInformation($@"*** Running IFTTT trigger: {callbackUrl} ...");
            // call out to ifttt webhook to shut off wemo
            var iftttResponse = await _client.GetAsync(callbackUrl);

            log.LogInformation(await iftttResponse.Content.ReadAsStringAsync());
        }
    }
}
