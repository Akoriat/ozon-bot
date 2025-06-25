using Bl.Interfaces;
using DAL.Interfaces;
using DAL.Models;

namespace Bl.Implementations
{
    public class ReviewDataStoreBl : IReviewDataStoreBl
    {
        private readonly IReviewDataStore _reviewDataStore;
        public ReviewDataStoreBl(IReviewDataStore reviewDataStore)
        {
            _reviewDataStore = reviewDataStore;
        }
        public async Task AddReviewAsync(Review review)
        {
            await _reviewDataStore.AddReviewAsync(review);
        }

        public async Task AddReviewsAsync(IList<Review> reviews, CancellationToken cancellation)
        {
            await _reviewDataStore.AddReviewsAsync(reviews, cancellation);
        }

        public async Task<Review> GetReviewAsync(int id)
        {
            return await _reviewDataStore.GetReviewAsync(id);
        }

        public async Task<IList<Review>> GetReviewsAsync()
        {
            return await _reviewDataStore.GetReviewsAsync();
        }

        public Review GetNewestReview()
        {
            return _reviewDataStore.GetNewestReview();
        }

        Task<Review> IReviewDataStoreBl.GetReviewByUniqueKey(string key)
        {
            var temp = key.Split('_');
            var date = DateOnly.ParseExact(temp[1], "dd.MM.yyyy");
            var time = TimeOnly.Parse(temp[2]);
            var name = temp[3];
            return _reviewDataStore.GetReviewByUniqueKey(date, time, name);
        }

        public void AddReviews(IList<Review> reviews)
        {
            _reviewDataStore.AddReviews(reviews);
        }
    }
}
