namespace MindMap.Undo;

/// <summary>元に戻せる 1 操作。</summary>
public interface IUndoableAction
{
    void Undo();

    void Redo();
}

/// <summary>ラムダで組み立てる汎用の操作。操作ごとにクラスを増やさずに済む。</summary>
public sealed class DelegateUndoableAction(Action undo, Action redo) : IUndoableAction
{
    public void Undo() => undo();

    public void Redo() => redo();
}

public sealed class UndoStack
{
    private readonly Stack<IUndoableAction> _undo = new();
    private readonly Stack<IUndoableAction> _redo = new();

    /// <summary>履歴の増減を伝える。ボタンの有効/無効を更新するために使う。</summary>
    public event Action? Changed;

    /// <summary>Undo/Redo の実行中。この間の変更は履歴に積まない。</summary>
    public bool IsApplying { get; private set; }

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    public void Push(IUndoableAction action)
    {
        // Undo によって起きた変更を新しい操作として積むと履歴が壊れる。
        if (IsApplying)
        {
            return;
        }

        _undo.Push(action);
        _redo.Clear();
        Changed?.Invoke();
    }

    public void Undo()
    {
        if (!CanUndo)
        {
            return;
        }

        var action = _undo.Pop();
        Apply(action.Undo);
        _redo.Push(action);
        Changed?.Invoke();
    }

    public void Redo()
    {
        if (!CanRedo)
        {
            return;
        }

        var action = _redo.Pop();
        Apply(action.Redo);
        _undo.Push(action);
        Changed?.Invoke();
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
        Changed?.Invoke();
    }

    private void Apply(Action action)
    {
        IsApplying = true;
        try
        {
            action();
        }
        finally
        {
            IsApplying = false;
        }
    }
}
