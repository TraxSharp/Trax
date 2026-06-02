using Trax.Effect.Services.ServiceTrain;

namespace Trax.Samples.Bookworm.Trains.Lending.BorrowBook;

public interface IBorrowBookTrain : IServiceTrain<BorrowBookInput, BorrowBookOutput>;
