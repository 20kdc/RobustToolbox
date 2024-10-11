using System;
using System.Threading.Tasks;

namespace Robust.Shared.Network
{
    /// <summary>
    /// The server version of the INetManager.
    /// </summary>
    public interface IServerNetManager : INetManager
    {
        public delegate Task<NetApproval> NetApprovalDelegate(NetApprovalEventArgs eventArgs);

        byte[]? CryptoPublicKey { get; }
        AuthMode Auth { get;  }
        Func<string, Task<NetUserId?>>? AssignUserIdCallback { get; set; }
        NetApprovalDelegate? HandleApprovalCallback { get; set; }

        /// <summary>
        ///     Disconnects this channel from the remote peer.
        /// </summary>
        /// <param name="channel">NetChannel to disconnect.</param>
        /// <param name="reason">Reason why it was disconnected.</param>
        void DisconnectChannel(INetChannel channel, string reason);

        /// <summary>
        /// NewKey: Put authhash into authentication cache.
        /// </summary>
        void NewKeyPutHash(string authHash, Guid guid)
        {
            // Default must be here to reduce conflicts with integration testing
        }
    }
}
