using Trax.Effect.Services.ServiceTrain;

namespace Trax.Samples.Bookworm.Trains.Lending.ReturnBook;

public interface IReturnBookTrain : IServiceTrain<ReturnBookInput, ReturnBookOutput>;
