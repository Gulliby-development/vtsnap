using System.Text;

namespace Vtsnap;

public abstract record VtEvent(int Pos);

public sealed record PrintEvent(int Pos, int CodePoint) : VtEvent(Pos);

public sealed record ControlEvent(int Pos, int CodePoint) : VtEvent(Pos);

public sealed record EscEvent(int Pos, string Intermediates, char Final, string Raw) : VtEvent(Pos);

public sealed record CsiEvent(
    int Pos,
    char Marker,
    int[] Params,
    string Intermediates,
    char Final,
    bool Malformed,
    string Raw) : VtEvent(Pos);

public sealed record OscEvent(int Pos, string Text, string Raw) : VtEvent(Pos);

public sealed record StringEvent(int Pos, string Raw) : VtEvent(Pos);

public sealed record UnterminatedEvent(int Pos, string What) : VtEvent(Pos);

public sealed class VtParser
{
    private enum ParserState
    {
        Ground,
        Escape,
        Csi,
        Osc,
        String
    }

    private ParserState _state;
    private int _position;
    private int _sequenceStart;
    private StringBuilder _raw = new();
    private StringBuilder _escapeIntermediates = new();
    private StringBuilder _csiParams = new();
    private StringBuilder _csiIntermediates = new();
    private StringBuilder _oscText = new();
    private char _csiMarker;
    private bool _csiMalformed;
    private bool _csiCanHaveMarker;
    private bool _escapePending;
    private int _pendingEscapePosition;

    public List<VtEvent> Feed(IReadOnlyList<int> codePoints)
    {
        ArgumentNullException.ThrowIfNull(codePoints);
        var events = new List<VtEvent>();

        for (int index = 0; index < codePoints.Count; index++)
        {
            int codePoint = codePoints[index];
            int position = _position;
            _position = unchecked(_position + 1);
            Process(codePoint, position, events);
        }

        return events;
    }

    public List<VtEvent> Finish()
    {
        var events = new List<VtEvent>();

        switch (_state)
        {
            case ParserState.Escape:
            case ParserState.Csi:
                events.Add(new UnterminatedEvent(_sequenceStart, "esc"));
                break;
            case ParserState.Osc:
                if (_escapePending)
                {
                    events.Add(CreateOscEvent());
                    events.Add(new UnterminatedEvent(_pendingEscapePosition, "esc"));
                }
                else
                {
                    events.Add(new UnterminatedEvent(_sequenceStart, "osc"));
                }

                break;
            case ParserState.String:
                events.Add(new UnterminatedEvent(
                    _escapePending ? _pendingEscapePosition : _sequenceStart,
                    _escapePending ? "esc" : "str"));
                break;
        }

        ResetToGround();
        return events;
    }

    private void Process(int codePoint, int position, List<VtEvent> events)
    {
        switch (_state)
        {
            case ParserState.Ground:
                ProcessGround(codePoint, position, events);
                break;
            case ParserState.Escape:
                ProcessEscape(codePoint, position, events);
                break;
            case ParserState.Csi:
                ProcessCsi(codePoint, position, events);
                break;
            case ParserState.Osc:
                ProcessOsc(codePoint, position, events);
                break;
            case ParserState.String:
                ProcessString(codePoint, position, events);
                break;
        }
    }

    private void ProcessGround(int codePoint, int position, List<VtEvent> events)
    {
        if (codePoint == 0x1B)
        {
            StartEscape(position);
        }
        else if (codePoint < 0x20)
        {
            events.Add(new ControlEvent(position, codePoint));
        }
        else if (codePoint != 0x7F)
        {
            events.Add(new PrintEvent(position, codePoint));
        }
    }

    private void ProcessEscape(int codePoint, int position, List<VtEvent> events)
    {
        if (codePoint is 0x18 or 0x1A)
        {
            ResetToGround();
            return;
        }

        if (codePoint == 0x1B)
        {
            StartEscape(position);
            return;
        }

        if (codePoint < 0x20)
        {
            events.Add(new ControlEvent(position, codePoint));
            return;
        }

        if (codePoint == 0x7F)
        {
            return;
        }

        if (codePoint >= 0x80)
        {
            ResetToGround();
            ProcessGround(codePoint, position, events);
            return;
        }

        if (codePoint is >= 0x20 and <= 0x2F)
        {
            _escapeIntermediates.Append(Text.FromCodePoint(codePoint));
            AppendRaw(codePoint);
            return;
        }

        if (codePoint is >= 0x30 and <= 0x7E)
        {
            if (_escapeIntermediates.Length == 0)
            {
                switch (codePoint)
                {
                    case '[':
                        _raw.Append('[');
                        StartCsi();
                        return;
                    case ']':
                        _raw.Append(']');
                        StartOsc();
                        return;
                    case 'P':
                    case 'X':
                    case '^':
                    case '_':
                        _raw.Append((char)codePoint);
                        StartString();
                        return;
                }
            }

            AppendRaw(codePoint);
            events.Add(new EscEvent(
                _sequenceStart,
                _escapeIntermediates.ToString(),
                (char)codePoint,
                _raw.ToString()));
            ResetToGround();
        }
    }

    private void ProcessCsi(int codePoint, int position, List<VtEvent> events)
    {
        if (codePoint is 0x18 or 0x1A)
        {
            ResetToGround();
            return;
        }

        if (codePoint == 0x1B)
        {
            StartEscape(position);
            return;
        }

        if (codePoint < 0x20)
        {
            events.Add(new ControlEvent(position, codePoint));
            return;
        }

        if (codePoint == 0x7F)
        {
            return;
        }

        if (codePoint >= 0x80)
        {
            ResetToGround();
            ProcessGround(codePoint, position, events);
            return;
        }

        if (codePoint is >= 0x40 and <= 0x7E)
        {
            AppendRaw(codePoint);
            events.Add(new CsiEvent(
                _sequenceStart,
                _csiMarker,
                ParseParams(_csiParams.ToString()),
                _csiIntermediates.ToString(),
                (char)codePoint,
                _csiMalformed,
                _raw.ToString()));
            ResetToGround();
            return;
        }

        AppendRaw(codePoint);
        if (_csiCanHaveMarker)
        {
            _csiCanHaveMarker = false;
            if (codePoint is '<' or '=' or '>' or '?')
            {
                _csiMarker = (char)codePoint;
                return;
            }
        }

        if (codePoint is >= '0' and <= '9' or ';')
        {
            if (_csiIntermediates.Length > 0)
            {
                _csiMalformed = true;
            }

            _csiParams.Append((char)codePoint);
        }
        else if (codePoint is >= 0x20 and <= 0x2F)
        {
            _csiIntermediates.Append((char)codePoint);
        }
        else
        {
            _csiMalformed = true;
        }
    }

    private void ProcessOsc(int codePoint, int position, List<VtEvent> events)
    {
        if (_escapePending)
        {
            if (codePoint == '\\')
            {
                events.Add(CreateOscEvent());
                ResetToGround();
                return;
            }

            int escapePosition = _pendingEscapePosition;
            events.Add(CreateOscEvent());
            StartEscape(escapePosition);
            ProcessEscape(codePoint, position, events);
            return;
        }

        if (codePoint is 0x18 or 0x1A)
        {
            ResetToGround();
            return;
        }

        if (codePoint == 0x07)
        {
            events.Add(CreateOscEvent());
            ResetToGround();
            return;
        }

        if (codePoint == 0x1B)
        {
            _escapePending = true;
            _pendingEscapePosition = position;
            return;
        }

        if (codePoint < 0x20)
        {
            events.Add(new ControlEvent(position, codePoint));
            return;
        }

        if (codePoint == 0x7F)
        {
            return;
        }

        AppendOscContent(codePoint);
    }

    private void ProcessString(int codePoint, int position, List<VtEvent> events)
    {
        if (_escapePending)
        {
            if (codePoint == '\\')
            {
                events.Add(new StringEvent(_sequenceStart, _raw.ToString()));
                ResetToGround();
                return;
            }

            int escapePosition = _pendingEscapePosition;
            ResetToGround();
            StartEscape(escapePosition);
            ProcessEscape(codePoint, position, events);
            return;
        }

        if (codePoint is 0x18 or 0x1A)
        {
            ResetToGround();
            return;
        }

        if (codePoint == 0x1B)
        {
            _escapePending = true;
            _pendingEscapePosition = position;
            return;
        }

        if (codePoint < 0x20)
        {
            events.Add(new ControlEvent(position, codePoint));
        }
    }

    private void StartEscape(int position)
    {
        ResetToGround();
        _state = ParserState.Escape;
        _sequenceStart = position;
        _raw.Append("<ESC>");
    }

    private void StartCsi()
    {
        _state = ParserState.Csi;
        _csiCanHaveMarker = true;
    }

    private void StartOsc()
    {
        _state = ParserState.Osc;
    }

    private void StartString()
    {
        _state = ParserState.String;
    }

    private OscEvent CreateOscEvent() =>
        new(_sequenceStart, _oscText.ToString(), _raw.ToString());

    private void AppendOscContent(int codePoint)
    {
        string text = Text.FromCodePoint(codePoint);
        _oscText.Append(text);
        _raw.Append(text);
    }

    private void AppendRaw(int codePoint)
    {
        _raw.Append(Text.FromCodePoint(codePoint));
    }

    private static int[] ParseParams(string parameterText)
    {
        if (parameterText.Length == 0)
        {
            return Array.Empty<int>();
        }

        var values = new List<int>();
        int value = 0;
        bool hasDigits = false;

        foreach (char character in parameterText)
        {
            if (character == ';')
            {
                values.Add(hasDigits ? value : -1);
                value = 0;
                hasDigits = false;
                continue;
            }

            int digit = character - '0';
            value = value > (65535 - digit) / 10
                ? 65535
                : (value * 10) + digit;
            hasDigits = true;
        }

        values.Add(hasDigits ? value : -1);
        return values.ToArray();
    }

    private void ResetToGround()
    {
        _state = ParserState.Ground;
        _sequenceStart = 0;
        _raw.Clear();
        _escapeIntermediates.Clear();
        _csiParams.Clear();
        _csiIntermediates.Clear();
        _oscText.Clear();
        _csiMarker = '\0';
        _csiMalformed = false;
        _csiCanHaveMarker = false;
        _escapePending = false;
        _pendingEscapePosition = 0;
    }
}
