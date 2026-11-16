// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using TrakHound.Http;
using TrakHound.Services;

namespace TrakHound.Instance.Http.Tests
{
    /// <summary>
    /// A Service Response that was never set has a Status Code of 0, which would be written as "HTTP/1.1 0" (an invalid status line
    /// that .NET and .NET Framework clients refuse to read). The Service Controllers answer it with a real HTTP status instead
    /// </summary>
    public class ServiceControllerStatusTests
    {
        [NonController]
        private sealed class TestServiceController : TrakHoundHttpServiceControllerBase
        {
            public TestServiceController() : base(null)
            {
                ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
            }

            public IActionResult Process(TrakHoundServiceResponse response) => ProcessServiceResponse(response);

            public IActionResult Process<TResult>(TrakHoundServiceResponse<TResult> response) => ProcessServiceResponse(response);

            public async Task<int> ProcessStream(TrakHoundServiceResponse response)
            {
                await ProcessServiceResponse(Response, response);
                return Response.StatusCode;
            }

            public async Task<int> ProcessStream<TResult>(TrakHoundServiceResponse<TResult> response)
            {
                await ProcessServiceResponse(Response, response);
                return Response.StatusCode;
            }
        }

        private static async Task<int> GetStatusCode(IActionResult result)
        {
            // A ContentResult needs MVC's services to execute, but the status it writes is its StatusCode
            if (result is ContentResult contentResult) return contentResult.StatusCode ?? 200;

            var context = new ActionContext { HttpContext = new DefaultHttpContext() };
            context.HttpContext.Response.Body = new MemoryStream();
            await result.ExecuteResultAsync(context);
            return context.HttpContext.Response.StatusCode;
        }


        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(100)]
        [InlineData(600)]
        public async Task A_Status_Code_That_Is_Not_An_Http_Status_Is_Answered_As_503(int statusCode)
        {
            var controller = new TestServiceController();

            Assert.Equal(503, await GetStatusCode(controller.Process(new TrakHoundServiceResponse(statusCode))));
            Assert.Equal(503, await GetStatusCode(controller.Process(new TrakHoundServiceResponse<string[]>(statusCode: statusCode))));
            Assert.Equal(503, await new TestServiceController().ProcessStream(new TrakHoundServiceResponse(statusCode)));
            Assert.Equal(503, await new TestServiceController().ProcessStream(new TrakHoundServiceResponse<string[]>(statusCode: statusCode)));
        }

        [Fact]
        public async Task A_Default_Response_Is_Answered_As_503()
        {
            // What a Service Client returned when there was no Router (ex. an Instance with no Services)
            Assert.Equal(503, await GetStatusCode(new TestServiceController().Process(default(TrakHoundServiceResponse<string[]>))));
        }

        [Theory]
        [InlineData(200)]
        [InlineData(400)]
        [InlineData(404)]
        [InlineData(405)]
        [InlineData(500)]
        [InlineData(503)]
        [InlineData(504)]
        public async Task An_Http_Status_Code_Is_Kept(int statusCode)
        {
            Assert.Equal(statusCode, await GetStatusCode(new TestServiceController().Process(new TrakHoundServiceResponse(statusCode))));
            Assert.Equal(statusCode, await GetStatusCode(new TestServiceController().Process(new TrakHoundServiceResponse<string[]>(statusCode: statusCode))));
            Assert.Equal(statusCode, await new TestServiceController().ProcessStream(new TrakHoundServiceResponse<string[]>(statusCode: statusCode)));
        }
    }
}
