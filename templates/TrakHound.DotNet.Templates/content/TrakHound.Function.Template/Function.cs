using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TrakHound;
using TrakHound.Functions;

// Describe each Input Parameter: the Admin interface shows a field for it, the Instance applies DefaultValue and refuses a Run
// without a Required Parameter. Remove this example or add more (Type, Options and Secret are also available)
[FunctionParameter("name", Description = "Who to greet", DefaultValue = "World")]
public class Function : TrakHoundFunction
{
    // The Instance creates the Function with this constructor, then sets Client, Volume and Variables
    public Function(ITrakHoundFunctionConfiguration configuration) : base(configuration) { }

    protected override Task<TrakHoundFunctionResponse> OnRun(IReadOnlyDictionary<string, string> parameters, CancellationToken cancellationToken)
    {
        Log(TrakHoundLogLevel.Information, $"Running UserPackageId Function for {parameters["name"]}..");

        return Task.FromResult(Ok("UserPackageId Function Completed"));
    }
}
