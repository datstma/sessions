using System.Threading.Tasks;

namespace Sessions.App.Services;

public interface IAppIconSource
{
    Task<byte[]?> GetIconAsync(string executablePath);
}
