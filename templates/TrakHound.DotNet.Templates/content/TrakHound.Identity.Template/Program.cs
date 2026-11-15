using System;
using TrakHound.Debug;
using TrakHound.Security;

// Test the Identity Provider without an Instance. Its Parameters are "parameters" in trakhound.debug.json
//   dotnet run -- <token>                  Authenticate a Request with the X-Example-Token Header
//   dotnet run -- <username> <password>    Sign In with a Username and Password (Credentials feature)
var host = await TrakHoundDebugHost.Create();
var provider = host.CreateIdentityProvider<Module>();

var request = new TrakHoundDebugIdentityRequest();
var context = host.CreateIdentityContext(request);

TrakHoundIdentityResult result;
if (args.Length >= 2)
{
    Console.WriteLine($"Sign In : {args[0]}");
    result = await provider.SignIn(context, new TrakHoundIdentityCredentials(args[0], args[1]));
}
else
{
    if (args.Length == 1) request.Headers["X-Example-Token"] = args[0];
    Console.WriteLine("Authenticate");
    result = await provider.Authenticate(context);
}

Console.WriteLine();
Console.WriteLine("RESULT:");
Console.WriteLine("--------------");
Console.WriteLine($"Status = {result.Status}");
if (!string.IsNullOrEmpty(result.Message)) Console.WriteLine($"Message = {result.Message}");
if (result.Identity != null)
{
    Console.WriteLine($"UserId = {result.Identity.UserId}");
    Console.WriteLine($"Name = {result.Identity.Name}");
    Console.WriteLine($"Roles = {string.Join(", ", result.Identity.Roles ?? Array.Empty<string>())}");
}
