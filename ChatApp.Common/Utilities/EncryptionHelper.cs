using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace ChatApp.Common.Utilities
{
    public static class EncryptionHelper
    {
        // В реальном приложении ключ и IV НЕ ДОЛЖНЫ быть хардкодом!
        // Их нужно генерировать безопасно и обмениваться между сторонами (например, с помощью асимметричного шифрования).
        // Это делается исключительно для демонстрации в дипломной работе :)
        private static readonly byte[] Key;
        private static readonly byte[] IV;

        static EncryptionHelper()
        {
            string keyString = "MySuperSecretKeyForChatApp123456";
            if (keyString.Length != 32)
            {
                throw new ArgumentException("Key string must be 32 characters long for AES-256.");
            }
            Key = Encoding.UTF8.GetBytes(keyString);

            string ivString = "MyIVForAES123456";
            if (ivString.Length != 16)
            {
                throw new ArgumentException("IV string must be 16 characters long for AES.");
            }
            IV = Encoding.UTF8.GetBytes(ivString);
        }

        public static string Encrypt(string plainText)
        {
            if (string.IsNullOrEmpty(plainText))
                return plainText;

            using (Aes aesAlg = Aes.Create())
            {
                aesAlg.Key = Key;
                aesAlg.IV = IV;
                aesAlg.Mode = CipherMode.CBC;
                aesAlg.Padding = PaddingMode.PKCS7;

                ICryptoTransform encryptor = aesAlg.CreateEncryptor(aesAlg.Key, aesAlg.IV);

                using (MemoryStream msEncrypt = new MemoryStream())
                {
                    using (CryptoStream csEncrypt = new CryptoStream(msEncrypt, encryptor, CryptoStreamMode.Write))
                    {
                        using (StreamWriter swEncrypt = new StreamWriter(csEncrypt))
                        {
                            swEncrypt.Write(plainText);
                        }
                        return Convert.ToBase64String(msEncrypt.ToArray());
                    }
                }
            }
        }

        public static string Decrypt(string cipherText)
        {
            if (string.IsNullOrEmpty(cipherText))
                return cipherText;

            byte[] cipherBytes = Convert.FromBase64String(cipherText);

            using (Aes aesAlg = Aes.Create())
            {
                aesAlg.Key = Key;
                aesAlg.IV = IV;
                aesAlg.Mode = CipherMode.CBC;
                aesAlg.Padding = PaddingMode.PKCS7;

                ICryptoTransform decryptor = aesAlg.CreateDecryptor(aesAlg.Key, aesAlg.IV);

                using (MemoryStream msDecrypt = new MemoryStream(cipherBytes))
                {
                    using (CryptoStream csDecrypt = new CryptoStream(msDecrypt, decryptor, CryptoStreamMode.Read))
                    {
                        using (StreamReader srDecrypt = new StreamReader(csDecrypt))
                        {
                            return srDecrypt.ReadToEnd();
                        }
                    }
                }
            }
        }
    }
}