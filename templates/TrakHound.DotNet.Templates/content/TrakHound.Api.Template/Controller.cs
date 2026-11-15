using System.Threading.Tasks;
using TrakHound;
using TrakHound.Api;

public class Controller : TrakHoundApiController
{
    [ApiGet]
    public Task<TrakHoundApiResponse> Get([FromQuery] string name)
    {
        return Task.FromResult(Ok("UserPackageId : Get : " + System.DateTime.Now.ToString("o") + " : " + name));
    }

    [ApiGet("{name}")]
    public Task<TrakHoundApiResponse> GetWithRouteParameter([FromRoute] string name)
    {
        return Task.FromResult(Ok("UserPackageId : GetWithRouteParameter : " + System.DateTime.Now.ToString("o") + " : " + name));
    }
}
