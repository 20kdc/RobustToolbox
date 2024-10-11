using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Nodes;
using System.Web;
using JWT;
using JWT.Algorithms;
using JWT.Builder;
using JWT.Exceptions;
using JWT.Serializers;
using Robust.Shared;
using Robust.Shared.Utility;
using Robust.Shared.IoC;

namespace Robust.Server.ServerStatus;

internal sealed partial class StatusHost
{
    private async Task<bool> HandleNewKey(IStatusHandlerContext context)
    {
        var url = context.Url;
        if (url.AbsolutePath != "/newkeyauth")
        {
            return false;
        }

        if (!context.RequestHeaders.TryGetValue("X-NewKey-JWT", out var jwt))
        {
            await context.RespondErrorAsync(HttpStatusCode.Unauthorized);
            return true;
        }

        if (!context.RequestHeaders.TryGetValue("X-NewKey-Pubkey", out var userPublicKeyStr))
        {
            await context.RespondErrorAsync(HttpStatusCode.Unauthorized);
            return true;
        }

        // Alright, now we get to the fun part: figuring out who this is.
        JwtParsedFields json;
        ECDsa userPublicKey;
        try
        {
            var serializer = new JsonNetSerializer();
            var provider = new UtcDateTimeProvider();
            var validator = new JwtValidator(serializer, provider);
            var urlEncoder = new JwtBase64UrlEncoder();

            userPublicKey = ECDsa.Create();
            userPublicKey.ImportFromPem(Encoding.UTF8.GetString(Convert.FromBase64String(userPublicKeyStr!)));

            var algorithm = new ES256Algorithm(userPublicKey);
            var decoder = new JwtDecoder(serializer, validator, urlEncoder, algorithm);

            json = decoder.DecodeToObject<JwtParsedFields>(jwt);
            // _httpSawmill.Debug("NewKey: Decoded JWT: " + decoder.Decode(jwt));
        }
        catch (Exception e)
        {
            await context.RespondAsync(e.ToString(), HttpStatusCode.Unauthorized);
            return true;
        }

        var myAudience = _netManager.CryptoPublicKey != null
                    ? Convert.ToBase64String(_netManager.CryptoPublicKey)
                    : null;

        if (json.Aud != myAudience)
        {
            await context.RespondAsync("Server public key mismatch", HttpStatusCode.Unauthorized);
            return true;
        }

        var userPublicKeyX509Der = userPublicKey.ExportSubjectPublicKeyInfo();

        var incHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        incHash.AppendData(userPublicKeyX509Der);
        incHash.AppendData(Encoding.UTF8.GetBytes(_cfg.GetCVar(CVars.AuthNewKeySalt)));
        var hash = incHash.GetHashAndReset();

        // Alright, now we convert this hash to a GUID.
        // This is the opposing statement to the & 0x7F in NetManager.ServerAuth.cs
        // That kicks server GUIDs into one namespace, this kicks decentralized GUIDs into another.
        hash[7] |= 0x80;
        var guid = new Guid(new ReadOnlySpan<byte>(hash).Slice(0, 16));
        var authHash = json.AuthHash;

        _netManager.NewKeyPutHash(authHash, guid);

        await context.RespondAsync($"Received and understood, {guid} {authHash}", (HttpStatusCode) 200);
        return true;
    }

    private sealed record JwtParsedFields(string? Aud, string AuthHash);
}
