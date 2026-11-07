namespace PCLCS.Common;

/// <summary>
/// 搜索家族的数据模型（ModBase.vb:681-717 逐字移植）。
/// VB 侧以继承薄壳（ModBase.SearchEntry(Of T) / ModBase.SearchSource）保持调用点类型零改动，本类型为共享核心。
/// </summary>

/// <summary>
/// 用于搜索的项目。
/// </summary>
/// <typeparam name="T">该项目对应的源数据类型。</typeparam>
public class SearchEntry<T>
{
    /// <summary>该项目对应的源数据。</summary>
    public T Item = default!;

    /// <summary>
    /// 该项目用于搜索的文本源。
    /// 在搜索时，会对每个文本源单独加权，但单个文本源内的多个别名只取最高的一个的相似度。
    /// </summary>
    //* 字段声明为 IEnumerable：VB 壳对象初始化器以 List<壳类型> 协变赋值（核心只读遍历，不改写元素）
    public IEnumerable<SearchSource> SearchSource = null!;

    /// <summary>相似度。</summary>
    public double Similarity;

    /// <summary>是否完全匹配。</summary>
    public bool AbsoluteRight;

    public override string ToString()
    {
        return Math.Round(Similarity, 3) + " - " + Item!.ToString();   //Item 为空时与 VB 一致抛 NRE
    }
}

/// <summary>
/// 单个用于搜索的文本源。
/// </summary>
public class SearchSource
{
    public string[] Aliases;

    public double Weight;

    public SearchSource(string[] aliases, double weight = 1)
    {
        Aliases = aliases;
        Weight = weight;
    }

    public SearchSource(string text, double weight = 1)
    {
        Aliases = [text];
        Weight = weight;
    }

}