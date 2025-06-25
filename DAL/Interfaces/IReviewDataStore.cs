using DAL.Models;

namespace DAL.Interfaces
{
    public interface IReviewDataStore
    {
        public Task<IList<Review>> GetReviewsAsync();
        public Task AddReviewsAsync(IList<Review> reviews, CancellationToken cancellation);
        public Task<Review> GetReviewAsync(int id);
        public Task AddReviewAsync(Review review);
        public Task<Review> GetReviewByUniqueKey(DateOnly date, TimeOnly time, string reviewName);
        public Review GetNewestReview();
        public void AddReviews(IList<Review> reviews);
    }
}
