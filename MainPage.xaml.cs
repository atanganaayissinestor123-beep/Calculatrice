using System.Globalization;

// IMPORTANT : ce namespace doit correspondre à celui de x:Class dans MainPage.xaml
namespace Calculatrice;

public partial class MainPage : ContentPage
{
    const int MaxDigits = 15;

    string _current = "0";      // nombre affiché / en cours de saisie
    decimal? _left;             // premier opérande
    string? _op;                // opérateur en attente
    string _lastExpr = "";      // ex. "12 + 3 =" affiché après un calcul
    bool _newEntry = true;      // le prochain chiffre démarre un nouveau nombre
    bool _error;
    bool _landscape;
    double _baseFont = 56;

    public MainPage()
    {
        InitializeComponent();

        // Le label du résultat occupe au moins la largeur visible => alignement à droite
        ResultScroll.SizeChanged += (_, _) => ResultLabel.MinimumWidthRequest = ResultScroll.Width;

        // Taille de police du résultat adaptée à l'afficheur
        DisplayBorder.SizeChanged += (_, _) =>
        {
            _baseFont = Math.Clamp(Math.Min(DisplayBorder.Height * 0.38, DisplayBorder.Width * 0.14), 22, 64);
            UpdateDisplay();
        };

        // Taille de police des boutons adaptée à la taille du clavier
        Keypad.SizeChanged += (_, _) =>
        {
            if (Keypad.Width <= 0 || Keypad.Height <= 0) return;
            double cell = Math.Min(Keypad.Height / 5, Keypad.Width / 4);
            double size = Math.Clamp(cell * 0.38, 14, 32);
            foreach (var b in Keypad.Children.OfType<Button>())
                b.FontSize = size;
        };

        UpdateDisplay();
    }

    // ---------- Adaptation portrait / paysage ----------
    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);
        if (width <= 0 || height <= 0) return;

        bool landscape = width > height;
        if (landscape == _landscape) return;
        _landscape = landscape;

        RootGrid.RowDefinitions.Clear();
        RootGrid.ColumnDefinitions.Clear();

        if (landscape)
        {
            // Afficheur à gauche, clavier à droite
            RootGrid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
            RootGrid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1.6, GridUnitType.Star)));
            RootGrid.RowDefinitions.Add(new RowDefinition(GridLength.Star));
            Grid.SetRow(DisplayBorder, 0); Grid.SetColumn(DisplayBorder, 0);
            Grid.SetRow(Keypad, 0);        Grid.SetColumn(Keypad, 1);
        }
        else
        {
            // Afficheur en haut, clavier en bas
            RootGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            RootGrid.RowDefinitions.Add(new RowDefinition(new GridLength(2, GridUnitType.Star)));
            RootGrid.RowDefinitions.Add(new RowDefinition(new GridLength(5, GridUnitType.Star)));
            Grid.SetRow(DisplayBorder, 0); Grid.SetColumn(DisplayBorder, 0);
            Grid.SetRow(Keypad, 1);        Grid.SetColumn(Keypad, 0);
        }
    }

    // ---------- Gestionnaires d'événements ----------
    void OnDigit(object? sender, EventArgs e)
    {
        if (_error) Reset();
        string d = ((Button)sender!).Text;

        if (_newEntry)
        {
            if (_op == null) _lastExpr = "";
            _current = d;
            _newEntry = false;
        }
        else if (_current == "0") _current = d;
        else if (_current == "-0") _current = "-" + d;
        else if (_current.Count(char.IsDigit) < MaxDigits) _current += d;

        UpdateDisplay();
    }

    void OnDecimal(object? sender, EventArgs e)
    {
        if (_error) Reset();

        if (_newEntry)
        {
            if (_op == null) _lastExpr = "";
            _current = "0.";
            _newEntry = false;
        }
        else if (!_current.Contains('.') && _current.Count(char.IsDigit) < MaxDigits)
            _current += ".";

        UpdateDisplay();
    }

    void OnOperator(object? sender, EventArgs e)
    {
        if (_error) Reset();
        string op = ((Button)sender!).Text;

        if (_op != null && !_newEntry)
        {
            // Enchaînement : 2 + 3 + => calcule d'abord 2 + 3
            if (!TryApply(out decimal r)) return;
            _left = r;
            _current = Fmt(r);
        }
        else if (_op == null)
        {
            _left = Parse(_current);
            _lastExpr = "";
        }
        // sinon : on remplace simplement l'opérateur

        _op = op;
        _newEntry = true;
        UpdateDisplay();
    }

    void OnEquals(object? sender, EventArgs e)
    {
        if (_error || _op == null || _left == null) return;

        decimal a = _left.Value;
        decimal b = Parse(_current);
        string op = _op;

        if (!TryApply(out decimal r)) return;

        _lastExpr = $"{Fmt(a)} {op} {Fmt(b)} =";
        _current = Fmt(r);
        _left = null;
        _op = null;
        _newEntry = true;
        UpdateDisplay();
    }

    void OnClear(object? sender, EventArgs e)
    {
        Reset();
        UpdateDisplay();
    }

    void OnBackspace(object? sender, EventArgs e)
    {
        if (_error) { Reset(); UpdateDisplay(); return; }
        if (_newEntry) return; // on n'efface pas un résultat

        _current = _current[..^1];
        if (_current is "" or "-") _current = "0";
        UpdateDisplay();
    }

    void OnSign(object? sender, EventArgs e)
    {
        if (_error) { Reset(); UpdateDisplay(); return; }

        if (_newEntry && _op != null)
        {
            _current = "-0";
            _newEntry = false;
        }
        else if (_current != "0")
        {
            _current = _current.StartsWith('-') ? _current[1..] : "-" + _current;
        }
        UpdateDisplay();
    }

    void OnPercent(object? sender, EventArgs e)
    {
        if (_error) { Reset(); UpdateDisplay(); return; }

        decimal cur = Parse(_current);
        // 200 + 10 % => 10 % de 200 = 20 ; sinon simple division par 100
        decimal value = (_op != null && _left != null) ? _left.Value * cur / 100 : cur / 100;

        _current = Fmt(Math.Round(value, 10));
        _newEntry = _op == null;
        UpdateDisplay();
    }

    // ---------- Logique ----------
    bool TryApply(out decimal result)
    {
        result = 0;
        decimal a = _left ?? 0;
        decimal b = Parse(_current);

        if (_op == "÷" && b == 0)
        {
            SetError("Division par zéro impossible");
            return false;
        }

        try
        {
            result = _op switch
            {
                "+" => a + b,
                "−" => a - b,
                "×" => a * b,
                "÷" => a / b,
                _ => b
            };
            result = Math.Round(result, 10);
            return true;
        }
        catch (OverflowException)
        {
            SetError("Nombre trop grand");
            return false;
        }
    }

    void SetError(string message)
    {
        _error = true;
        _current = message;
        _left = null;
        _op = null;
        _lastExpr = "";
        _newEntry = true;
        UpdateDisplay();
    }

    void Reset()
    {
        _current = "0";
        _left = null;
        _op = null;
        _lastExpr = "";
        _newEntry = true;
        _error = false;
    }

    static decimal Parse(string s) => decimal.Parse(s, NumberStyles.Number, CultureInfo.InvariantCulture);

    static string Fmt(decimal d) => d.ToString("0.############################", CultureInfo.InvariantCulture);

    // ---------- Affichage ----------
    void UpdateDisplay()
    {
        ResultLabel.Text = _current;

        ExpressionLabel.Text = _lastExpr != ""
            ? _lastExpr
            : (_op != null && _left != null ? $"{Fmt(_left.Value)} {_op}" : "");

        StatusLabel.Text = _error ? "Erreur" : (_op != null ? $"Opérateur : {_op}" : "");

        // Réduit la police quand le texte est long
        int len = _current.Length;
        ResultLabel.FontSize = len <= 9 ? _baseFont : Math.Max(16, _baseFont * 9 / len);

        // Garde la fin du nombre visible
        MainThread.BeginInvokeOnMainThread(async () =>
            await ResultScroll.ScrollToAsync(ResultScroll.ContentSize.Width, 0, false));
    }
}
