namespace AIMS.Server.Application.DTOs.Psd;

public class TextAssetsDto
{
    // ✅ 这里的 MainPanel 属性必须存在，PsdService 才能调用
    public MainPanelDto MainPanel { get; set; } = new();
    public InfoPanelDto InfoPanel { get; set; } = new();
}