// ReSharper disable once CheckNamespace
namespace BD.WTTS.Services.Implementation;

/// <summary>
/// DER 编码器
/// </summary>
/// <remarks>
/// .NET 未提供证书吊销列表(Certificate Revocation List)与 CRL Distribution Point 扩展的构造 API，
/// 此处按 RFC 5280 的 ASN.1 定义手工编码，字节布局与 OpenSSL 生成的产物一致。
/// </remarks>
static class DerEncoder
{
    /// <summary>
    /// 构造原始标签的上下文专用标签
    /// </summary>
    /// <param name="tagNumber">标签号</param>
    /// <returns></returns>
    public static byte ContextSpecificTag(byte tagNumber) => (byte)(0x80 | tagNumber);

    /// <summary>
    /// 构造复合标签的上下文专用标签
    /// </summary>
    /// <remarks>承载 SEQUENCE 等复合内容时必须使用复合标签，否则解析器无法读取内部结构</remarks>
    /// <param name="tagNumber">标签号</param>
    /// <returns></returns>
    public static byte ContextSpecificConstructedTag(byte tagNumber) => (byte)(0xA0 | tagNumber);

    /// <summary>
    /// 以指定标签编码内容
    /// </summary>
    /// <param name="tag">标签</param>
    /// <param name="content">内容</param>
    /// <returns></returns>
    public static byte[] Encode(byte tag, byte[] content)
    {
        var length = EncodeLength(content.Length);
        var result = new byte[1 + length.Length + content.Length];
        result[0] = tag;
        length.CopyTo(result, 1);
        content.CopyTo(result, 1 + length.Length);
        return result;
    }

    /// <summary>
    /// 编码长度
    /// </summary>
    /// <param name="length">长度</param>
    /// <returns></returns>
    static byte[] EncodeLength(int length)
    {
        if (length < 0x80)
        {
            return new[] { (byte)length };
        }
        var bytes = new List<byte>();
        var value = length;
        while (value > 0)
        {
            bytes.Insert(0, (byte)(value & 0xFF));
            value >>= 8;
        }
        bytes.Insert(0, (byte)(0x80 | bytes.Count));
        return bytes.ToArray();
    }

    /// <summary>
    /// 拼接多个编码块
    /// </summary>
    /// <param name="parts">编码块</param>
    /// <returns></returns>
    public static byte[] Concat(params byte[][] parts) => parts.SelectMany(x => x).ToArray();

    /// <summary>
    /// 编码 INTEGER
    /// </summary>
    /// <param name="value">值</param>
    /// <returns></returns>
    public static byte[] Integer(long value)
    {
        if (value == 0)
        {
            return new byte[] { 0x02, 0x01, 0x00 };
        }
        var bytes = new List<byte>();
        var remaining = value;
        while (remaining > 0)
        {
            bytes.Insert(0, (byte)(remaining & 0xFF));
            remaining >>= 8;
        }
        // 最高位为 1 时补 0，避免被解析成负数
        if ((bytes[0] & 0x80) != 0)
        {
            bytes.Insert(0, 0x00);
        }
        return Encode(0x02, bytes.ToArray());
    }

    /// <summary>
    /// 编码 OCTET STRING
    /// </summary>
    /// <param name="content">内容</param>
    /// <returns></returns>
    public static byte[] OctetString(byte[] content) => Encode(0x04, content);

    /// <summary>
    /// 编码 BIT STRING
    /// </summary>
    /// <param name="content">内容</param>
    /// <returns></returns>
    public static byte[] BitString(byte[] content) => Encode(0x03, Concat(new byte[] { 0x00 }, content));

    /// <summary>
    /// 编码 SEQUENCE
    /// </summary>
    /// <param name="parts">内容</param>
    /// <returns></returns>
    public static byte[] Sequence(params byte[][] parts) => Encode(0x30, Concat(parts));

    /// <summary>
    /// 编码 OBJECT IDENTIFIER
    /// </summary>
    /// <param name="encodedValue">已编码的内容</param>
    /// <returns></returns>
    public static byte[] ObjectIdentifier(byte[] encodedValue) => Encode(0x06, encodedValue);

    /// <summary>
    /// 编码 UTCTime
    /// </summary>
    /// <param name="value">时间</param>
    /// <returns></returns>
    public static byte[] UtcTime(DateTimeOffset value)
    {
        var text = value.UtcDateTime.ToString("yyMMddHHmmss", CultureInfo.InvariantCulture) + "Z";
        return Encode(0x17, Encoding.ASCII.GetBytes(text));
    }
}
