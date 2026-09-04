using System;
using System.IO;
using System.Security.Cryptography;

namespace MapEngine.Core.Utilities;

/// <summary>
/// 内容哈希计算工具：用于实现内容寻址存储
/// </summary>
public static class ContentHasher
{
    /// <summary>
    /// 计算文件的 SHA256 哈希值（截取前 16 位）
    /// </summary>
    public static string ComputeHash(string filePath)
    {
        using var sha256 = SHA256.Create();
        using var stream = File.OpenRead(filePath);
        var hashBytes = sha256.ComputeHash(stream);
        var hashString = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();

        // 截取前 16 位（64 位十六进制）
        return hashString.Substring(0, 16);
    }

    /// <summary>
    /// 计算字节数组的 SHA256 哈希值（截取前 16 位）
    /// </summary>
    public static string ComputeHash(byte[] data)
    {
        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(data);
        var hashString = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();

        return hashString.Substring(0, 16);
    }

    /// <summary>
    /// 计算流的 SHA256 哈希值（截取前 16 位）
    /// </summary>
    public static string ComputeHash(Stream stream)
    {
        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(stream);
        var hashString = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();

        return hashString.Substring(0, 16);
    }
}
