using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace ChatApp.Common.Utilities
{
    public static class EncryptionHelper
    {
        // Увага: У реальному застосунку ключ та IV НЕ ПОВИННІ бути захардкодженими!
        // Їх потрібно генерувати безпечно та обмінювати між сторонами (наприклад, за допомогою асиметричного шифрування).
        // Це робиться тут виключно для демонстрації в дипломній роботі.
        private static readonly byte[] Key;
        private static readonly byte[] IV;

        static EncryptionHelper() // Статичний конструктор для ініціалізації Key та IV
        {
            // Переконайтеся, що довжина цього рядка ТОЧНО 32 символи (для AES-256)
            string keyString = "MySuperSecretKeyForChatApp123456"; // 32 символи
            if (keyString.Length != 32)
            {
                throw new ArgumentException("Key string must be 32 characters long for AES-256.");
            }
            Key = Encoding.UTF8.GetBytes(keyString);

            // Переконайтеся, що довжина цього рядка ТОЧНО 16 символів (для AES)
            string ivString = "MyIVForAES123456"; // 16 символів
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
                aesAlg.Mode = CipherMode.CBC; // Режим шифрування
                aesAlg.Padding = PaddingMode.PKCS7; // Схема доповнення

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