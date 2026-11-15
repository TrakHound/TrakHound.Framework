// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Api;
using TrakHound.Functions;
using TrakHound.Services;
using TrakHound.Workers;

namespace TrakHound.Hosting.Tests
{
    public class SampleController : TrakHoundApiController
    {
        [ApiGet("greeting", Description = "Returns the greeting Parameter.")]
        public TrakHoundApiResponse Greeting() => Ok(Configuration.GetParameter("greeting"));

        [ApiGet("headers", Description = "Returns the Header Parameters it was sent.")]
        public TrakHoundApiResponse Headers(
            [FromHeader("X-TrakHound-Api", Description = "The caller's access token")] string token,
            [FromHeader("Authorization")] string authorization = "withheld",
            [FromHeader(Description = "A number")] int count = -1
            )
        {
            return Ok($"{token ?? "null"}|{authorization}|{count}");
        }

        [ApiStream("ticks", Description = "Sends one tick.")]
        public ITrakHoundStream<TrakHoundApiResponse> Ticks()
        {
            var stream = new TrakHoundStream<TrakHoundApiResponse>();
            _ = Task.Run(async () =>
            {
                await Task.Delay(200); // Let the client subscribe
                await stream.Push(Ok("tick"));
            });
            return stream;
        }
    }

    public class SampleWorker : TrakHoundWorker
    {
        public static int Interval;
        public static bool Running;


        public SampleWorker(ITrakHoundWorkerConfiguration configuration) : base(configuration) { }


        protected override void OnStart()
        {
            Interval = Configuration.GetParameter<int>("interval");
            Running = true;
            Log(TrakHoundLogLevel.Information, "Worker Started");
        }

        protected override void OnStop()
        {
            Running = false;
        }
    }

    [FunctionParameter("name", Required = true)]
    [FunctionParameter("punctuation", DefaultValue = "!")]
    public class SampleFunction : TrakHoundFunction
    {
        public SampleFunction(ITrakHoundFunctionConfiguration configuration) : base(configuration) { }

        protected override Task<TrakHoundFunctionResponse> OnRun(IReadOnlyDictionary<string, string> parameters, CancellationToken cancellationToken)
        {
            return Task.FromResult(Ok($"{Configuration.GetParameter("greeting")} {parameters["name"]}{parameters["punctuation"]}"));
        }
    }

    public class SampleService : TrakHoundService
    {
        public bool Disposed { get; private set; }


        public SampleService(ITrakHoundServiceConfiguration configuration) : base(configuration) { }


        public string GetName() => Configuration.GetParameter("name");

        protected override void OnDisposed() => Disposed = true;
    }
}
