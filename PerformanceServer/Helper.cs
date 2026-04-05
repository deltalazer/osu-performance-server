// Copyright (c) 2025 GooGuTeam
// Licensed under the MIT Licence. See the LICENCE file in the repository root for full licence text.

using osu.Game.Rulesets;
using osu.Game.Rulesets.Osu;
using System.Security.Cryptography;

namespace PerformanceServer
{
    public static class Helper
    {
        public static string ComputeMd5(string input)
        {
            byte[] inputBytes = System.Text.Encoding.UTF8.GetBytes(input);
            byte[] hashBytes = MD5.HashData(inputBytes);
            return Convert.ToHexString(hashBytes).ToLowerInvariant();
        }
    }
}
