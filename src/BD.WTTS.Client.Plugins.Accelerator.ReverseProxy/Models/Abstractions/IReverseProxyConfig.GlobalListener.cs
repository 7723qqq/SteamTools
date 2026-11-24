// https://github.com/dotnetcore/FastGithub/blob/2.1.4/FastGithub.Configuration/GlobalListener.cs

using System.Net.NetworkInformation;

// ReSharper disable once CheckNamespace
namespace BD.WTTS.Models.Abstractions;

partial interface IReverseProxyConfig
{
    private static readonly IPGlobalProperties global = IPGlobalProperties.GetIPGlobalProperties();
    private static readonly object portLock = new();
    private static HashSet<int> tcpListenPorts = new();
    private static HashSet<int> udpListenPorts = new();
    private static bool listenersInitialized;

    const int SshPortDefault = 22;
    static int? sshPort;

    /// <summary>
    /// SSH 端口
    /// </summary>
    static int SshPort => GetPort(ref sshPort, SshPortDefault);

    const int GitHubDesktopPort = 9418;
    static int? gitPort;

    /// <summary>
    /// Git 端口
    /// </summary>
    static int GitPort => GetPort(ref gitPort, GitHubDesktopPort);

    const int HttpPortDefault = 80;
    static int? httpPort;

    /// <summary>
    /// Http 端口
    /// </summary>
    static int HttpPort => GetPort(ref httpPort, HttpPortDefault);

    const int HttpsPortDefault = 443;
    static int? httpsPort;

    /// <summary>
    /// Https 端口
    /// </summary>
    static int HttpsPort => GetPort(ref httpsPort, HttpsPortDefault);

    const int CrlPortDefault = 26502;
    static int? crlPort;

    /// <summary>
    /// 证书吊销列表分发端口
    /// <para>叶子证书内嵌此端口的 CRL Distribution Point 地址，端口不可用时顺延到下一个空闲端口</para>
    /// </summary>
    static int CrlPort => GetPort(ref crlPort, CrlPortDefault);

    static int GetPort(ref int? port, int defaultPort)
    {
        lock (portLock)
        {
            port ??= GetAvailableTcpPortCore(defaultPort);
            return port.Value;
        }
    }

    public static void RefreshPorts()
    {
        RefreshListenPorts();
        lock (portLock)
        {
            sshPort = GetAvailableTcpPortCore(SshPortDefault);
            gitPort = GetAvailableTcpPortCore(GitHubDesktopPort);
            httpPort = GetAvailableTcpPortCore(HttpPortDefault);
            httpsPort = GetAvailableTcpPortCore(HttpsPortDefault);
            crlPort = GetAvailableTcpPortCore(CrlPortDefault);
        }
    }

    /// <summary>
    /// 获取已监听的端口
    /// </summary>
    /// <param name="func"></param>
    /// <returns></returns>
    private static HashSet<int> GetListenPorts(Func<IPEndPoint[]> func)
    {
        var hashSet = new HashSet<int>();
        try
        {
            foreach (var endpoint in func())
            {
                hashSet.Add(endpoint.Port);
            }
        }
        catch (Exception)
        {
        }
        return hashSet;
    }

    static void RefreshListenPorts()
    {
        lock (portLock)
        {
            tcpListenPorts = GetListenPorts(global.GetActiveTcpListeners);
            udpListenPorts = GetListenPorts(global.GetActiveUdpListeners);
            listenersInitialized = true;
        }
    }

    static void EnsureListenersInitialized()
    {
        lock (portLock)
        {
            if (!listenersInitialized)
                RefreshListenPorts();
        }
    }

    /// <summary>
    /// 是否可用的 TCP 端口
    /// </summary>
    /// <param name="port"></param>
    /// <returns></returns>
    public static bool IsAvailableTcp(int port)
    {
        EnsureListenersInitialized();
        lock (portLock)
        {
            return tcpListenPorts.Contains(port) == false;
        }
    }

    /// <summary>
    /// 是否可用的 UDP 端口
    /// </summary>
    /// <param name="port"></param>
    /// <returns></returns>
    public static bool IsAvailableUdp(int port)
    {
        EnsureListenersInitialized();
        lock (portLock)
        {
            return udpListenPorts.Contains(port) == false;
        }
    }

    /// <summary>
    /// 是否可用的 TCP 和 UDP 端口
    /// </summary>
    /// <param name="port"></param>
    /// <returns></returns>
    public static bool IsAvailableTcpAndUdp(int port) => IsAvailableTcp(port) && IsAvailableUdp(port);

    /// <summary>
    /// 获取一个随机的可用的 TCP 端口
    /// </summary>
    /// <param name="minPort"></param>
    /// <returns></returns>
    public static int GetAvailableTcpPort(int minPort)
    {
        RefreshListenPorts();
        return GetAvailableTcpPortCore(minPort);
    }

    static int GetAvailableTcpPortCore(int minPort) => GetAvailablePort(IsAvailableTcp, minPort);

    /// <summary>
    /// 获取一个随机的可用的 UDP 端口
    /// </summary>
    /// <param name="minPort"></param>
    /// <returns></returns>
    public static int GetAvailableUdpPort(int minPort)
    {
        RefreshListenPorts();
        return GetAvailablePort(IsAvailableUdp, minPort);
    }

    /// <summary>
    /// 获取一个随机的可用的 TCP 和 UDP 端口
    /// </summary>
    /// <param name="minPort"></param>
    /// <returns></returns>
    public static int GetAvailableTcpAndUdpPort(int minPort)
    {
        RefreshListenPorts();
        return GetAvailablePort(IsAvailableTcpAndUdp, minPort);
    }

    static int GetAvailablePort(Func<int, bool> isAvailable, int minPort)
    {
        for (var port = minPort; port < IPEndPoint.MaxPort; port++)
        {
            if (isAvailable(port) == true)
            {
                return port;
            }
        }
        throw new ApplicationException("Failed to get available ports. There are no available ports.");
    }
}
