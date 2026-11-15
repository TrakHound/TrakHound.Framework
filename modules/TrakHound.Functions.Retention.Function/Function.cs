using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TrakHound;
using TrakHound.Clients;
using TrakHound.Clients.Services;
using TrakHound.Functions;
using TrakHound.Services.Tables;
using TrakHound.Volumes;

[FunctionParameter("days", Description = "How many days of Function Runs and Logs to keep (older ones are deleted)", Type = TrakHoundFunctionParameterType.Integer, DefaultValue = "30")]
public class Function : TrakHoundFunction
{
    private const string _schema = "trakhound";

    public Function(ITrakHoundFunctionConfiguration configuration, ITrakHoundClient client, ITrakHoundVolume volume) : base(configuration, client, volume) { }

    protected async override Task<TrakHoundFunctionResponse> OnRun(IReadOnlyDictionary<string, string> parameters)
    {
        Log(TrakHoundLogLevel.Information, "Running TrakHound.Functions.Retention.Function Function..");

        var days = int.Parse(parameters["days"]); // Checked by the Instance (an Integer, 30 when not passed)
        if (days < 1) return BadRequest("days must be at least 1");

        var timestamp = DateTime.Today.AddDays(-days);

        // Run inputs and outputs have no start column, so they are deleted (by the keys of their Run) before the Runs
        var builder = new TrakHoundTableRequestBuilder();
        builder.Parameters.Add("cutoff", TrakHoundTableDataType.Int64, timestamp.ToUnixTime());

        // Tables that don't exist yet are skipped
        var tables = (await Client.Services.Tables.List()).Result?.Where(o => o.Schema == _schema).Select(o => o.Name).ToHashSet() ?? new HashSet<string>();

        void Delete(string table, string where)
        {
            if (tables.Contains(table)) builder.AppendDeleteBatched(_schema, table, where);
        }

        var runIsExpired = $"exists (select 1 from \"{_schema}\".\"function_run\" as \"run\" where \"run\".\"instance_id\" = \"target\".\"instance_id\" and \"run\".\"function_id\" = \"target\".\"function_id\" and \"run\".\"id\" = \"target\".\"run_id\" and \"run\".\"start\" < @cutoff)";

        Delete("function_run_output", runIsExpired);
        Delete("function_run_input", runIsExpired);
        Delete("function_run_detail", "\"target\".\"start\" < @cutoff");
        Delete("function_run_trigger", "\"target\".\"start\" < @cutoff");
        Delete("function_run", "\"target\".\"start\" < @cutoff");
        Delete("function_log", "\"target\".\"timestamp\" < @cutoff");

        if (builder.Count == 0) return Ok("TrakHound.Functions.Retention.Function Function Completed");

        // Run without a Transaction so each batch of 5000 rows commits on its own and locks are held briefly.
        // Deleting a large backlog can take longer than the default command timeout
        builder.Timeout = 3600;
        var databaseResult = await Client.Services.Tables.ExecuteNonQuery(builder.Build(useTransaction: false));
        if (databaseResult.Success)
        {
            Log(TrakHoundLogLevel.Information, $"Rows Deleted Successfully : {databaseResult.Message}");
        }
        else
        {
            Log(TrakHoundLogLevel.Error, $"Error Deleting Rows : {databaseResult.Message}");
        }

        return Ok("TrakHound.Functions.Retention.Function Function Completed");
    }
}
