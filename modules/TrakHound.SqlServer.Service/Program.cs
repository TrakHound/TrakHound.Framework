using TrakHound;
using TrakHound.Services;
using TrakHound.SqlServer.Driver;

var configuration = new TrakHoundServiceDriverConfiguration();
configuration.SetParameter("server", "localhost");
configuration.SetParameter("database", "trakhound");
configuration.SetParameter("user", "trakhound");
configuration.SetParameter("password", "trakhound");

var driver = new SqlServerTableService(configuration);

var query = "select * from [debug];select * from [gps_associates];";

var response = await driver.ExecuteQuery(query);
if (response.Success)
{
    Console.WriteLine(response.Result.ToJson(true));
}
else
{
    Console.WriteLine(response.Message);
}