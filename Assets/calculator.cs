using System;
using System.Collections;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using org.mariuszgromada.math.mxparser;

public class Calculator : MonoBehaviour
{
    [SerializeField] private UIDocument document;

    [SerializeField] private StyleSheet styleSheet;
    
    // Some of these could be done with a regex, but I'm not doing that for performance reasons. (Not that it matters)
    private VisualElement _root;
    private Label _equationLabel;
    private Label _resultLabel;
    private string _equation;
    private bool _showResult;
    private int _openParenCount;
    private DecimalState _canAddDecimal;
    private StyleLength _defaultEquationFontSize;
    private StyleLength _defaultResultFontSize;

    enum DecimalState
    {
        CanAdd,
        CannotAdd,
        Pending
    };
    
    private void Start()
    {
        StartCoroutine(Load());
    }

    private void OnValidate()
    {
        if (Application.isPlaying) return;
        StartCoroutine(Load());
    }

    private IEnumerator Load()
    {
        yield return null;
        
        // Load the stylesheet
        _root = document.rootVisualElement;
        _root.styleSheets.Add(styleSheet);
    
        // Get buttons/labels
        _equationLabel = _root.Q<Label>("equation");
        _resultLabel = _root.Q<Label>("result");
        _defaultEquationFontSize = _equationLabel.resolvedStyle.fontSize;
        _defaultResultFontSize = _resultLabel.resolvedStyle.fontSize;

        var numberButtons = _root.Query<Button>(className: "number-button").ToList();
        var operatorButtons = _root.Query<Button>(className: "operator").ToList();

        var equalButton = _root.Q<Button>("equal");
        
        // Add event listeners
        foreach (var btn in numberButtons)
        {
            string number = btn.text.Trim();
            btn.clicked += () =>
            {
                if (number == ".") // Handle decimal placement
                { 
                    if (_canAddDecimal == DecimalState.CannotAdd) return;
                    _canAddDecimal = DecimalState.CannotAdd;
                    if (_equation.Length == 0 || _equation.Last() == '(') _equation += "0";
                }
                else if (_canAddDecimal == DecimalState.Pending)
                {
                    _canAddDecimal = DecimalState.CanAdd;
                }
                _equation += number;
                UpdateLabels();
            };
        }

        foreach (var btn in operatorButtons)
        {
            string btnText = btn.text;
            Action onClick = GetOpAction(btnText);
            btn.clicked += onClick;
            if (!"CE ( ) ←".Contains(btnText)) // We don't need to show the result in these cases
            {
                btn.clicked += () =>
                {
                    if (!CanAddOperator(doReplacement: false)) return;
                    _showResult = true;
                    _canAddDecimal = DecimalState.Pending;
                    UpdateLabels();
                };
            };
        }

        equalButton.clicked += CompleteCalculation;
        _showResult = false;
    }
    
    // Returns true if an operator can be added.
    // If allowAtStart is true, an operator can be added at the start of a term.
    private bool CanAddOperator(bool allowAtStart = false, bool expandedReplace = false, bool doReplacement = true)
    {
        if (allowAtStart && (_equation.Length == 0 || _equation.Last() == '('))
        {
            return false;
        }

        if (doReplacement && _equation.Last() == '.')
        {
            _equation = _equation.Substring(0, _equation.Length - 1);
        }
        
        if(doReplacement && "+-*/".Contains(_equation.Last()))
        {
            _equation = _equation.Substring(0, _equation.Length - 1);
        }
        
        if(doReplacement && expandedReplace && "!%".Contains(_equation.Last()))
        {
            _equation = _equation.Substring(0, _equation.Length - 1);
        }

        return true;
    }
    
    /*
     * Returns an action that will be called when an operation button is clicked.
     * It would probably be better to define actions outside (perhaps in a class or dictionary)
     */
    private Action GetOpAction(string op)
    {
        switch (op)
        {
            case "CE": // Clear everything
                return () =>
                {
                    _equation = "";
                    UpdateLabels(); // Usually we'd use some Action wrapper class to prevent repeating this.
                };
            case "( )": // Parentheses logic
                return () =>
                {
                    var parenType = "("; // Default to open paren
                    if (_openParenCount > 0 && !"+-*/(".Contains(_equation.Last())) // If we can close a paren
                    {
                        parenType = ")";
                        _openParenCount--;
                        _canAddDecimal = DecimalState.Pending;
                    }
                    else
                    {
                        _openParenCount++;
                        _canAddDecimal = DecimalState.CanAdd;
                    }
                    _equation += parenType;
                    UpdateLabels();
                };
            case "←": // Backspace
                return () =>
                {
                    if (_equation.Length == 0) return;
                    if (_equation.Last() == '(') _openParenCount--;
                    if (_equation.Last() == ')') _openParenCount++;
                    if (_equation.Last() == '.') _canAddDecimal = DecimalState.CanAdd;
                    _equation = _equation.Substring(0, _equation.Length - 1);
                    UpdateLabels();
                };
            case "\u215fₓ": // 1/x
                return () =>
                {
                    if(!CanAddOperator()) return;
                    _equation += "^(-1)";
                    UpdateLabels();
                };
            case "x\u00b2": // x^2
                return () =>
                {
                    if(!CanAddOperator()) return;
                    _equation += "^2";
                    UpdateLabels();
                };
            case "√x": // sqrt(x)
                return () =>
                {
                    if(!CanAddOperator()) return;
                    _equation += "^(1/2)";
                    UpdateLabels();
                };
            case "\u00f7": // division
                return () =>
                {
                    if(!CanAddOperator()) return;
                    _equation += "/";
                    UpdateLabels();
                };
            case "\u00d7": // multiplication
                return () =>
                {
                    if(!CanAddOperator()) return;
                    _equation += "*";
                    UpdateLabels();
                };
            case "+":
                return () =>
                {
                    CanAddOperator();
                    _equation += op;
                    UpdateLabels();
                };
            case "%":
            case "!":
                return () =>
                {
                    if(!CanAddOperator(expandedReplace: true)) return;
                    _equation += op;
                    UpdateLabels();
                };
            case "-": // subtraction / negation
                return () =>
                {
                    CanAddOperator(allowAtStart: true);
                    _equation += op;
                    UpdateLabels();
                };
            default:
                throw new Exception("Invalid operator");
        }
    }

    /*
     * Updates the equation and result labels.
     */
    private void UpdateLabels()
    {
        // If the equation is empty, reset everything.
        if (_equation.Length == 0)
        {
            _equationLabel.text = "";
            _resultLabel.text = "";
            _openParenCount = 0;
            _showResult = false;
            _canAddDecimal = DecimalState.CanAdd;
            return;
        }
        
        _equationLabel.style.fontSize = _defaultEquationFontSize;
        // Update the equation label
        while (_equationLabel.MeasureTextSize(_equation, 0, 0, 0 ,0)[0] > _equationLabel.resolvedStyle.width)
        {
            _equationLabel.style.fontSize = 0.99f * _equationLabel.resolvedStyle.fontSize;
        } 
        
        _equationLabel.text = _equation;
        
        Debug.Log(_equationLabel.MeasureTextSize(_equation, 0, 0, 0 ,0));
        Debug.Log(_equationLabel.resolvedStyle.width);
        
        
        // Replace * and / with their unicode equivalents
        _equationLabel.text = _equationLabel.text.Replace("*", "\u00d7").Replace("/", "\u00f7");
        
        // Calculate the result
        var result = new Expression(_equation).calculate();
        if (!double.IsNaN(result)) // If the result is NaN, don't update.
        {
            if (_showResult)
            {
                _resultLabel.text = result.ToString();
                _resultLabel.style.fontSize = _defaultResultFontSize;
                while (_resultLabel.MeasureTextSize(_resultLabel.text, 0, 0, 0 ,0)[0] > _resultLabel.resolvedStyle.width)
                {
                    _resultLabel.style.fontSize = 0.99f * _resultLabel.resolvedStyle.fontSize;
                }
            }
            else
            {
                _resultLabel.text = "";
            }
        }
    }
    
    private void CompleteCalculation()
    {
        if (_equation.Length == 0) return;
        if (_equation.Last() == '.') _equation = _equation.Substring(0, _equation.Length - 1);
        _equation.Trim('(');
        _equation = new Expression(_equation).calculate().ToString();
        _showResult = true;
        _canAddDecimal = DecimalState.Pending;
        UpdateLabels();
    }
}
