using System.Globalization;

namespace Vtsnap;

public sealed class Terminal
{
    private readonly Grid _mainGrid;
    private Grid _alternateGrid;
    private readonly List<Cell[]> _scrollback = new();
    private readonly List<Finding> _findings = new();
    private readonly IReadOnlyList<Finding> _findingView;

    private int _cursorRow;
    private int _cursorCol;
    private int _savedRow;
    private int _savedCol;
    private Style _style = Style.Default;
    private Style _savedStyle = Style.Default;
    private string _title = string.Empty;
    private int _regionTop;
    private int _regionBottom;
    private bool _alternateScreen;
    private bool _cursorVisible = true;
    private bool _autoWrap = true;
    private bool _wrapPending;

    public Terminal(int cols, int rows)
    {
        _mainGrid = new Grid(cols, rows);
        _alternateGrid = new Grid(cols, rows);
        _regionBottom = rows - 1;
        _findingView = _findings.AsReadOnly();
    }

    public IReadOnlyList<Finding> Findings => _findingView;

    public bool AltScreen => _alternateScreen;

    public bool CursorVisible => _cursorVisible;

    public Style CurrentStyle => _style;

    public bool RegionIsFull => _regionTop == 0 && _regionBottom == _mainGrid.Rows - 1;

    private Grid ActiveGrid => _alternateScreen ? _alternateGrid : _mainGrid;

    public void Apply(VtEvent ev)
    {
        ArgumentNullException.ThrowIfNull(ev);

        switch (ev)
        {
            case PrintEvent print:
                Print(print.CodePoint);
                break;
            case ControlEvent control:
                ApplyControl(control.CodePoint);
                break;
            case EscEvent escape:
                ApplyEscape(escape);
                break;
            case CsiEvent csi:
                ApplyCsi(csi);
                break;
            case OscEvent osc:
                ApplyOsc(osc);
                break;
            case StringEvent controlString:
                AddIgnored(controlString.Pos, controlString.Raw);
                break;
            case UnterminatedEvent:
                break;
        }
    }

    public Snapshot Snapshot(bool withScrollback)
    {
        Cell[][] screenRows = ActiveGrid.SnapshotRows();
        if (!withScrollback || _alternateScreen || _scrollback.Count == 0)
        {
            return new Snapshot(
                _mainGrid.Cols,
                _mainGrid.Rows,
                _title,
                _cursorRow,
                _cursorCol,
                _cursorVisible,
                screenRows);
        }

        var lines = new Cell[_scrollback.Count + screenRows.Length][];
        for (int i = 0; i < _scrollback.Count; i++)
        {
            lines[i] = (Cell[])_scrollback[i].Clone();
        }

        Array.Copy(screenRows, 0, lines, _scrollback.Count, screenRows.Length);
        return new Snapshot(
            _mainGrid.Cols,
            _mainGrid.Rows,
            _title,
            _cursorRow,
            _cursorCol,
            _cursorVisible,
            lines);
    }

    private void Print(int codePoint)
    {
        if (_wrapPending && _autoWrap)
        {
            _cursorCol = 0;
            Index();
        }

        _wrapPending = false;
        ActiveGrid[_cursorRow, _cursorCol] = new Cell(codePoint, _style);
        if (_cursorCol == _mainGrid.Cols - 1)
        {
            _wrapPending = _autoWrap;
        }
        else
        {
            _cursorCol++;
        }
    }

    private void ApplyControl(int codePoint)
    {
        switch (codePoint)
        {
            case 0x08:
                _cursorCol = Math.Max(0, _cursorCol - 1);
                _wrapPending = false;
                break;
            case 0x09:
                int nextTabStop = ((_cursorCol / 8) + 1) * 8;
                _cursorCol = Math.Min(_mainGrid.Cols - 1, nextTabStop);
                _wrapPending = false;
                break;
            case 0x0A:
            case 0x0B:
            case 0x0C:
                _cursorCol = 0;
                Index();
                _wrapPending = false;
                break;
            case 0x0D:
                _cursorCol = 0;
                _wrapPending = false;
                break;
        }
    }

    private void ApplyEscape(EscEvent ev)
    {
        if (ev.Intermediates.Length == 0)
        {
            switch (ev.Final)
            {
                case '7':
                    SaveCursorAndStyle();
                    return;
                case '8':
                    RestoreCursorAndStyle();
                    _wrapPending = false;
                    return;
                case 'D':
                    Index();
                    _wrapPending = false;
                    return;
                case 'E':
                    _cursorCol = 0;
                    Index();
                    _wrapPending = false;
                    return;
                case 'M':
                    ReverseIndex();
                    _wrapPending = false;
                    return;
                case '=':
                case '>':
                    return;
            }
        }
        else if (ev.Intermediates.Length == 1 && ev.Intermediates[0] is '(' or ')' or '*' or '+')
        {
            return;
        }

        AddIgnored(ev.Pos, ev.Raw);
    }

    private void ApplyCsi(CsiEvent ev)
    {
        if (ev.Marker == '?' && !ev.Malformed && ev.Intermediates.Length == 0
            && ev.Final is 'h' or 'l')
        {
            ApplyPrivateModes(ev);
            return;
        }

        if (ev.Marker != '\0' || ev.Malformed || ev.Intermediates.Length != 0)
        {
            AddIgnored(ev.Pos, ev.Raw);
            return;
        }

        switch (ev.Final)
        {
            case 'A':
                _cursorRow = MoveRelative(_cursorRow, FirstParameter(ev.Params), _mainGrid.Rows);
                _wrapPending = false;
                break;
            case 'B':
                _cursorRow = MoveRelative(_cursorRow, FirstParameter(ev.Params), _mainGrid.Rows, increase: true);
                _wrapPending = false;
                break;
            case 'C':
                _cursorCol = MoveRelative(_cursorCol, FirstParameter(ev.Params), _mainGrid.Cols, increase: true);
                _wrapPending = false;
                break;
            case 'D':
                _cursorCol = MoveRelative(_cursorCol, FirstParameter(ev.Params), _mainGrid.Cols);
                _wrapPending = false;
                break;
            case 'G':
                SetAbsoluteColumn(ev, FirstParameter(ev.Params));
                _wrapPending = false;
                break;
            case 'd':
                SetAbsoluteRow(ev, FirstParameter(ev.Params));
                _wrapPending = false;
                break;
            case 'H':
            case 'f':
                SetAbsolutePosition(ev);
                _wrapPending = false;
                break;
            case 'J':
                EraseDisplay(ev, FirstParameter(ev.Params, 0));
                break;
            case 'K':
                EraseLine(ev, FirstParameter(ev.Params, 0));
                break;
            case 'L':
                EditRows(ev, insert: true);
                break;
            case 'M':
                EditRows(ev, insert: false);
                break;
            case 'S':
                ScrollUp(FirstParameter(ev.Params));
                break;
            case 'T':
                ScrollDown(FirstParameter(ev.Params));
                break;
            case 'r':
                SetScrollRegion(ev);
                break;
            case 's':
                if (ev.Params.Length == 0)
                {
                    SaveCursorAndStyle();
                }
                else
                {
                    AddIgnored(ev.Pos, ev.Raw);
                }

                break;
            case 'u':
                if (ev.Params.Length == 0)
                {
                    RestoreCursorAndStyle();
                    _wrapPending = false;
                }
                else
                {
                    AddIgnored(ev.Pos, ev.Raw);
                }

                break;
            case 'm':
                ApplySgr(ev);
                break;
            default:
                AddIgnored(ev.Pos, ev.Raw);
                break;
        }
    }

    private void ApplyPrivateModes(CsiEvent ev)
    {
        bool set = ev.Final == 'h';
        bool hasUnknownMode = false;
        foreach (int mode in ev.Params)
        {
            switch (mode)
            {
                case 7:
                    _autoWrap = set;
                    break;
                case 25:
                    _cursorVisible = set;
                    break;
                case 47:
                case 1047:
                case 1049:
                    _wrapPending = false;
                    SetAlternateScreen(set);
                    break;
                case 1:
                case 12:
                case 1000:
                case 1002:
                case 1003:
                case 1004:
                case 1006:
                case 2004:
                    break;
                default:
                    hasUnknownMode = true;
                    break;
            }
        }

        if (hasUnknownMode)
        {
            AddIgnored(ev.Pos, ev.Raw);
        }
    }

    private void SetAlternateScreen(bool enabled)
    {
        if (_alternateScreen == enabled)
        {
            return;
        }

        if (enabled)
        {
            SaveCursorAndStyle();
            _alternateGrid = new Grid(_mainGrid.Cols, _mainGrid.Rows);
            _alternateScreen = true;
        }
        else
        {
            _alternateScreen = false;
            RestoreCursorAndStyle();
        }
    }

    private void ApplyOsc(OscEvent ev)
    {
        int separator = ev.Text.IndexOf(';');
        if (separator < 0)
        {
            AddIgnored(ev.Pos, ev.Raw);
            return;
        }

        string command = ev.Text[..separator];
        if (command is "0" or "2")
        {
            _title = ev.Text[(separator + 1)..];
        }
        else
        {
            AddIgnored(ev.Pos, ev.Raw);
        }
    }

    private void ApplySgr(CsiEvent ev)
    {
        SgrResult result = Sgr.Apply(_style, ev.Params);
        _style = result.Style;
        if (result.Invalid)
        {
            string parameters = string.Join(
                ";",
                ev.Params.Select(parameter => parameter.ToString(CultureInfo.InvariantCulture)));
            _findings.Add(new Finding(
                ev.Pos,
                "VT005",
                $"invalid SGR parameters \"{parameters}\""));
        }
    }

    private void SetAbsoluteColumn(CsiEvent ev, int requestedColumn)
    {
        if (requestedColumn > _mainGrid.Cols)
        {
            AddFinding(
                ev.Pos,
                "VT004",
                $"column {Format(requestedColumn)} is outside the screen ({Text.Plural(_mainGrid.Cols, "column", "columns")})");
        }

        _cursorCol = Math.Min(requestedColumn, _mainGrid.Cols) - 1;
    }

    private void SetAbsoluteRow(CsiEvent ev, int requestedRow)
    {
        if (requestedRow > _mainGrid.Rows)
        {
            AddFinding(
                ev.Pos,
                "VT004",
                $"row {Format(requestedRow)} is outside the screen ({Text.Plural(_mainGrid.Rows, "row", "rows")})");
        }

        _cursorRow = Math.Min(requestedRow, _mainGrid.Rows) - 1;
    }

    private void SetAbsolutePosition(CsiEvent ev)
    {
        int requestedRow = ParameterOrDefault(ev.Params, 0, 1);
        int requestedColumn = ParameterOrDefault(ev.Params, 1, 1);
        if (requestedRow > _mainGrid.Rows || requestedColumn > _mainGrid.Cols)
        {
            AddFinding(
                ev.Pos,
                "VT004",
                $"position {Format(requestedRow)};{Format(requestedColumn)} is outside the screen "
                    + $"({Text.Plural(_mainGrid.Rows, "row", "rows")}, "
                    + $"{Text.Plural(_mainGrid.Cols, "column", "columns")})");
        }

        _cursorRow = Math.Min(requestedRow, _mainGrid.Rows) - 1;
        _cursorCol = Math.Min(requestedColumn, _mainGrid.Cols) - 1;
    }

    private void EraseDisplay(CsiEvent ev, int mode)
    {
        Grid grid = ActiveGrid;
        Cell blank = Grid.Blank(_style);
        switch (mode)
        {
            case 0:
                grid.FillRow(_cursorRow, _cursorCol, _mainGrid.Cols - _cursorCol, blank);
                for (int row = _cursorRow + 1; row < _mainGrid.Rows; row++)
                {
                    grid.FillRow(row, 0, _mainGrid.Cols, blank);
                }

                break;
            case 1:
                for (int row = 0; row < _cursorRow; row++)
                {
                    grid.FillRow(row, 0, _mainGrid.Cols, blank);
                }

                grid.FillRow(_cursorRow, 0, _cursorCol + 1, blank);
                break;
            case 2:
                grid.FillRegion(0, _mainGrid.Rows - 1, blank);
                break;
            case 3:
                grid.FillRegion(0, _mainGrid.Rows - 1, blank);
                _scrollback.Clear();
                break;
            default:
                AddIgnored(ev.Pos, ev.Raw);
                break;
        }
    }

    private void EraseLine(CsiEvent ev, int mode)
    {
        Grid grid = ActiveGrid;
        Cell blank = Grid.Blank(_style);
        switch (mode)
        {
            case 0:
                grid.FillRow(_cursorRow, _cursorCol, _mainGrid.Cols - _cursorCol, blank);
                break;
            case 1:
                grid.FillRow(_cursorRow, 0, _cursorCol + 1, blank);
                break;
            case 2:
                grid.FillRow(_cursorRow, 0, _mainGrid.Cols, blank);
                break;
            default:
                AddIgnored(ev.Pos, ev.Raw);
                break;
        }
    }

    private void EditRows(CsiEvent ev, bool insert)
    {
        if (_cursorRow < _regionTop || _cursorRow > _regionBottom)
        {
            return;
        }

        int count = FirstParameter(ev.Params);
        Cell fill = Grid.Blank(_style);
        if (insert)
        {
            ActiveGrid.InsertRows(_cursorRow, _regionBottom, count, fill);
        }
        else
        {
            ActiveGrid.DeleteRows(_cursorRow, _regionBottom, count, fill);
        }

        _cursorCol = 0;
        _wrapPending = false;
    }

    private void ScrollUp(int count)
    {
        Cell[][] removed = ActiveGrid.ScrollUp(_regionTop, _regionBottom, count, Grid.Blank(_style));
        if (!_alternateScreen && _regionTop == 0)
        {
            _scrollback.AddRange(removed);
        }
    }

    private void ScrollDown(int count)
    {
        ActiveGrid.ScrollDown(_regionTop, _regionBottom, count, Grid.Blank(_style));
    }

    private void SetScrollRegion(CsiEvent ev)
    {
        int top = ParameterOrDefault(ev.Params, 0, 1);
        int bottom = ParameterOrDefault(ev.Params, 1, _mainGrid.Rows);
        bottom = Math.Min(bottom, _mainGrid.Rows);
        if (top >= bottom || top > _mainGrid.Rows)
        {
            return;
        }

        _regionTop = top - 1;
        _regionBottom = bottom - 1;
        _cursorRow = 0;
        _cursorCol = 0;
        _wrapPending = false;
    }

    private void Index()
    {
        if (_cursorRow == _regionBottom)
        {
            ScrollUp(1);
        }
        else if (_cursorRow < _mainGrid.Rows - 1)
        {
            _cursorRow++;
        }
    }

    private void ReverseIndex()
    {
        if (_cursorRow == _regionTop)
        {
            ScrollDown(1);
        }
        else if (_cursorRow > 0)
        {
            _cursorRow--;
        }
    }

    private void SaveCursorAndStyle()
    {
        _savedRow = _cursorRow;
        _savedCol = _cursorCol;
        _savedStyle = _style;
    }

    private void RestoreCursorAndStyle()
    {
        _cursorRow = _savedRow;
        _cursorCol = _savedCol;
        _style = _savedStyle;
    }

    private void AddIgnored(int pos, string raw) =>
        _findings.Add(new Finding(pos, "VT001", $"ignored sequence \"{raw}\""));

    private void AddFinding(int pos, string code, string message) =>
        _findings.Add(new Finding(pos, code, message));

    private static int FirstParameter(int[] parameters, int defaultValue = 1) =>
        ParameterOrDefault(parameters, 0, defaultValue);

    private static int ParameterOrDefault(int[] parameters, int index, int defaultValue)
    {
        if (index >= parameters.Length || parameters[index] <= 0)
        {
            return defaultValue;
        }

        return parameters[index];
    }

    private static int MoveRelative(int current, int amount, int limit, bool increase = false)
    {
        long target = increase ? (long)current + amount : (long)current - amount;
        return (int)Math.Clamp(target, 0L, limit - 1L);
    }

    private static string Format(int value) => value.ToString(CultureInfo.InvariantCulture);
}
