using ShoppingPlatform.Models;

namespace ShoppingPlatform.Repositories
{
    public interface ISecretsRepository
    {
        Task<Secrets?> GetSecretsByAddedForAsync(string addedFor);
        Task UpsertSecretsAsync(Secrets secrets);
    }
}
