using CodexWidget.Runtime;

namespace CodexWidget.Web;

public interface ICodexWidgetRuntimeFactory
{
    CodexWidgetRuntime Create(CodexWidgetRuntimeOptions options);
}

public sealed class ProductionCodexWidgetRuntimeFactory : ICodexWidgetRuntimeFactory
{
    public CodexWidgetRuntime Create(CodexWidgetRuntimeOptions options)
    {
        return CodexWidgetRuntime.CreateProduction(options);
    }
}
