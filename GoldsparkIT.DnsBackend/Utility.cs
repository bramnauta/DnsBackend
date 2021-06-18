﻿using System.Linq;
using System.Security.Cryptography;

namespace GoldsparkIT.DnsBackend
{
    public static class Utility
    {
        public static string RandomString(int length, string allowedCharacters = @"abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-_=+!@#$%^&*()[{]}\|;:'""/?.>,<")
        {
            return new(new string(' ', length).Select(x => allowedCharacters[RandomNumberGenerator.GetInt32(0, allowedCharacters.Length)]).ToArray());
        }

        public static bool HasPriority(string type)
        {
            return type.ToLower() switch
            {
                "cert" => true,
                "mx" => true,
                "naptr" => true,
                "srv" => true,
                _ => false
            };
        }
    }
}