namespace MindMap.ViewModels;

/// <summary>未保存のドキュメントを閉じようとしたときの、利用者の答え。</summary>
public enum SaveChangesResult
{
    /// <summary>保存してから閉じる。</summary>
    Save,

    /// <summary>保存せずに閉じる。</summary>
    Discard,

    /// <summary>閉じるのをやめる。</summary>
    Cancel,
}
