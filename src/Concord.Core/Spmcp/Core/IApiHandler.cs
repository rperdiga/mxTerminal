using Microsoft.AspNetCore.Http;
using System.Threading.Tasks;

namespace Terminal.Spmcp.Core
{
    public interface IApiHandler
    {
        string Path { get; }
        string Method { get; }
        Task HandleAsync(HttpContext context);
    }
}
