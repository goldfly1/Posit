using Posit.Contracts.Ai;
using Posit.Contracts.Core;

namespace Posit.AI.Models;

/// <summary>
/// Gateway interface for model calls. All model calls go through Ollama
/// on localhost:11434. The :cloud suffix on model names is just an Ollama
/// tag — the gateway doesn't distinguish local vs cloud models.
/// </summary>
public interface IModelGateway
{
    Task<GenerationResult> GenerateAsync(ModelRoute route, PromptTemplate prompt, PhaseContext context, CancellationToken ct = default);
}