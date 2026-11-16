using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TrakHound;
using TrakHound.Clients;
using TrakHound.Extensions;
using TrakHound.Functions;
using TrakHound.Packages;
using TrakHound.Volumes;

[FunctionParameter("keep", Description = "How many of the latest versions of each Package to keep (older versions are deleted)", Type = TrakHoundFunctionParameterType.Integer, DefaultValue = "5")]
public class Function : TrakHoundFunction
{
    public Function(ITrakHoundFunctionConfiguration configuration, ITrakHoundClient client, ITrakHoundVolume volume) : base(configuration, client, volume) { }

    protected async override Task<TrakHoundFunctionResponse> OnRun(IReadOnlyDictionary<string, string> parameters)
    {
        Log(TrakHoundLogLevel.Information, "Running TrakHound.Packages.Retention.Function Function..");

        var limit = int.Parse(parameters["keep"]); // Checked by the Instance (an Integer, 5 when not passed)
        if (limit < 1) return BadRequest("keep must be at least 1");

        var organizations = await Client.Api.GetJson<IEnumerable<string>>("/packages/organizations");
        if (!organizations.IsNullOrEmpty())
        {
            foreach (var organization in organizations)
            {
                Log(TrakHoundLogLevel.Information, organization);
                Log(TrakHoundLogLevel.Information, "----------------------");

                var latestPackages = await Client.Api.GetJson<IEnumerable<TrakHoundPackageJson>>($"/packages/{organization}/latest");
                if (!latestPackages.IsNullOrEmpty())
                {
                    foreach (var package in latestPackages)
                    {
                        Log(TrakHoundLogLevel.Information, "- " + package.Id);

                        var versions = await Client.Api.GetJson<IEnumerable<string>>($"/packages/{organization}/{package.Id}/versions");
                        if (!versions.IsNullOrEmpty())
                        {
                            // Semantic Versioning order, so Prereleases are kept or deleted like any other version
                            foreach (var version in versions.OrderByDescending(o => o, VersionComparer.Default).Skip(limit))
                            {
                                var logLine = "=> " + version + " : Deleting..";
                                var error = false;

                                var packageUuid = TrakHoundPackage.GenerateUuid(package.Id, version);

                                var deleteRoute = $"/packages/{organization}";
                                deleteRoute = Url.AddQueryParameter(deleteRoute, "packageUuid", packageUuid);

                                var deleteResponse = await Client.Api.Delete(deleteRoute);
                                if (deleteResponse.Success)
                                {
                                    logLine += "SUCCESS";
                                }
                                else
                                {
                                    logLine += $"ERROR : {deleteResponse.StatusCode} : {deleteResponse.GetContentUtf8String()}";
                                    error = true;
                                }

                                if (error) Log(TrakHoundLogLevel.Error, logLine);
                                else Log(TrakHoundLogLevel.Information, logLine);
                            }
                        }
                    }
                }
            }
        }

        return Ok("TrakHound.Packages.Retention.Function Function Completed");
    }
}
