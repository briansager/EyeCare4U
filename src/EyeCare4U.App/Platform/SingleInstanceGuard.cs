using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;

namespace EyeCare4U.App.Platform;

public sealed class SingleInstanceGuard : IDisposable
{
    private readonly Mutex _mutex;
    public bool IsOwner { get; }
    private bool _disposed;
    public SingleInstanceGuard(string? name=null)
    {
        var sid=WindowsIdentity.GetCurrent().User?.Value??Environment.UserName;
        name??="Global\\EyeCare4U-"+Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sid)));
        _mutex=new Mutex(true,name,out var created); IsOwner=created;
    }
    public void Dispose() { if(_disposed) return; _disposed=true; if(IsOwner) _mutex.ReleaseMutex(); _mutex.Dispose(); }
}
