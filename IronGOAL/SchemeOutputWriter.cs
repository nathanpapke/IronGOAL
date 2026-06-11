using System.Text;

namespace IronGOAL;

internal sealed class SchemeOutputWriter : TextWriter
{
    private readonly TextWriter _inner;
    public SchemeOutputWriter(TextWriter inner) => _inner = inner;
    
    public override Encoding Encoding => _inner.Encoding;
    
    public override void Write(char value)
    {
        // Bare \n → \r\n. The only transformation needed.
        if (value == '\n')
            _inner.Write("\r\n");
        else
            _inner.Write(value);
    }
    
    public override void Write(char[] buffer, int index, int count)
    {
        // IronScheme's display writes in char-array chunks; handle them directly
        // to avoid the overhead of char-by-char virtual dispatch.
        for (int i = index; i < index + count; i++)
            Write(buffer[i]);
    }
    
    public override void Flush() => _inner.Flush();
}
