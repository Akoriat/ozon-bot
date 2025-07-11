using DAL.Models;

namespace Bl.Interfaces
{
    public interface IReviewDataStoreBl
    {
        public Task<IList<Review>> GetReviewsAsync();
        public Task AddReviewsAsync(IList<Review> reviews, CancellationToken cancellation);
        public void AddReviews(IList<Review> reviews);
        public Task<Review> GetReviewAsync(int id);
        public Task AddReviewAsync(Review review);
        public Task<Review> GetReviewByUniqueKey(string key);
        public Review GetNewestReview();
    }
}
