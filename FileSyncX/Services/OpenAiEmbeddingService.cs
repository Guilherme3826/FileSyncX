
using OpenAI.Embeddings;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace FileSyncX.Services;

/// <summary>
/// Implementação do serviço de embeddings utilizando a API OpenAI.
/// </summary>
public sealed class OpenAiEmbeddingService : IEmbeddingService
{
    // =========================================================
    // CONFIGURAÇÕES
    // =========================================================

    public const string ModelName = "text-embedding-3-small";

    public const int ExpectedDimensions = 1536;

    private readonly EmbeddingClient _client;

    // =========================================================
    // CONSTRUTOR
    // =========================================================
    private static string ObterChaveApi()
    {
        const string nomeVariavel = "OPENAI_API_KEY";

        // Primeiro: tenta obter a chave do processo atual.
        string? chave = Environment.GetEnvironmentVariable(
            nomeVariavel,
            EnvironmentVariableTarget.Process
        );

        // Segundo: procura nas variáveis do usuário do Windows.
        if (string.IsNullOrWhiteSpace(chave))
        {
            chave = Environment.GetEnvironmentVariable(
                nomeVariavel,
                EnvironmentVariableTarget.User
            );
        }

        // Terceiro: procura nas variáveis do sistema.
        if (string.IsNullOrWhiteSpace(chave))
        {
            chave = Environment.GetEnvironmentVariable(
                nomeVariavel,
                EnvironmentVariableTarget.Machine
            );
        }

        if (string.IsNullOrWhiteSpace(chave))
        {
            throw new InvalidOperationException(
                "A variável de ambiente OPENAI_API_KEY não foi encontrada. " +
                "Verifique as variáveis de ambiente do Windows."
            );
        }

        return chave;
    }
    public OpenAiEmbeddingService()
    {
        var apiKey = ObterChaveApi();

        _client = new EmbeddingClient(
            model: ModelName,
            apiKey: apiKey);
    }

    // =========================================================
    // GERAÇÃO DE EMBEDDING
    // =========================================================

    public async Task<float[]> GenerateEmbeddingAsync(
        string text,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        cancellationToken.ThrowIfCancellationRequested();

        // Define explicitamente as dimensões compatíveis
        // com as colunas vector(1536) do VecktorVault.
        var options = new EmbeddingGenerationOptions
        {
            Dimensions = ExpectedDimensions
        };

        // Solicita a geração do embedding à OpenAI.
        var response = await _client.GenerateEmbeddingAsync(
            input: text.Trim(),
            options: options,
            cancellationToken: cancellationToken);

        // Converte o resultado para um vetor float[].
        float[] embedding = response.Value
            .ToFloats()
            .ToArray();

        // =====================================================
        // VALIDAÇÃO DO VETOR
        // =====================================================

        if (embedding.Length != ExpectedDimensions)
        {
            throw new InvalidOperationException(
                $"Dimensão inválida do embedding. " +
                $"Esperado: {ExpectedDimensions}. " +
                $"Recebido: {embedding.Length}.");
        }

        if (embedding.Any(value => !float.IsFinite(value)))
        {
            throw new InvalidOperationException(
                "O embedding retornado contém valores inválidos.");
        }

        return embedding;
    }
}
