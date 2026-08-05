namespace MindMap.Services;

/// <summary>
/// リンク先の種類。ノードのタイトル脇に出すアイコンを決めるために使う。
/// <see cref="File"/> だけは関連付けられたアプリのアイコンを出し、
/// それ以外はアプリ内の線画アイコンで表す（見た目を揃えるため）。
/// </summary>
public enum LinkKind
{
    /// <summary>種類を決められないもの（独自のスキームなど）。</summary>
    Unknown,

    /// <summary>マインドマップのファイル。</summary>
    MindMap,

    /// <summary>http / https の URL。</summary>
    Web,

    /// <summary>mailto: のリンク。</summary>
    Mail,

    /// <summary>フォルダー。</summary>
    Folder,

    /// <summary>上記以外のファイル。</summary>
    File,
}
