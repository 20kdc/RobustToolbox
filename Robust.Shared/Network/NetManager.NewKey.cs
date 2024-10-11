using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Robust.Shared.IoC;
using Robust.Shared.Utility;

namespace Robust.Shared.Network;

public sealed partial class NetManager
{
    // Generated this way for compatibility with older versions.
    private static readonly string DisconnectReasonNewKeyError = "{\n\"reason\":\"NewKey token not received or didn't match.\\nPlease reconnect to this server from the launcher.\",\n\"redial\":true\n}";

    private readonly SemaphoreSlim _newkeyLock = new(1, 1);
    private readonly Dictionary<string, Guid> _newkeyDict = new();

    public Guid? NewKeyGetHash(string authHash)
    {
        _logger.Debug("NewKey: TryNewKeyServerAuthArrange(" + authHash + ")");
        using (var _ = _newkeyLock.WaitGuard())
        {
            if (_newkeyDict.TryGetValue(authHash, out var guid))
            {
                return guid;
            }
        }
        return null;
    }

    public void NewKeyPutHash(string authHash, Guid guid)
    {
        _logger.Debug("NewKey: Authorized AuthHash: " + authHash);

        using (var _ = _newkeyLock.WaitGuard())
        {
            _newkeyDict[authHash] = guid;
        }

        Task.Run(async () =>
        {
            await Task.Delay(60000);
            using (var _ = _newkeyLock.WaitGuard())
            {
                _newkeyDict.Remove(authHash);
            }
        });
    }
}
