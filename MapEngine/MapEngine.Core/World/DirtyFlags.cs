namespace MapEngine.Core;

/// <summary>
/// 场景变更的脏标记类别。World 在每次变更时打标,SystemScheduler 据此决定
/// 哪些 System 需要重算 —— 这是 P2 事件驱动增量重算的基础。
/// 用 [Flags] 以便一次变更打多个类别(如移动对象同时脏 Spatial+Vision)。
/// </summary>
[Flags]
public enum DirtyFlags
{
    None    = 0,
    /// <summary>层级结构变化(增删对象、改父子)。</summary>
    Hierarchy = 1 << 0,
    /// <summary>空间位置/包围盒变化(需重建索引、重算视野遮挡)。</summary>
    Spatial = 1 << 1,
    /// <summary>视野相关变化(墙/门/光源/视野组件)。</summary>
    Vision  = 1 << 2,
    /// <summary>渲染外观变化(贴图/颜色/透明度),不影响几何。</summary>
    Render  = 1 << 3,

    All = Hierarchy | Spatial | Vision | Render
}
