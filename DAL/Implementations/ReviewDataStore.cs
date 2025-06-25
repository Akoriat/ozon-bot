using DAL.Data;
using DAL.Interfaces;
using DAL.Models;
using Microsoft.EntityFrameworkCore;

namespace DAL.Implementations
{
    public class ReviewDataStore : IReviewDataStore
    {
        private readonly OzonBotDbContext _context;

        public ReviewDataStore(OzonBotDbContext context)
        {
            _context = context;
        }

        public async Task AddReviewAsync(Review review)
        {
            await _context.Reviews.AddAsync(review);
            await _context.SaveChangesAsync();
        }

        public async Task AddReviewsAsync(IList<Review> reviews, CancellationToken cancellation)
        {
            var ids = reviews.Select(r => r.Id).ToList();

            var existing = await _context.Reviews
                                         .Where(r => ids.Contains(r.Id))
                                         .ToDictionaryAsync(r => r.Id, cancellation);

            foreach (var review in reviews)
            {
                if (existing.TryGetValue(review.Id, out var tracked))
                {
                    _context.Entry(tracked).CurrentValues.SetValues(review);
                }
                else
                {
                    await _context.Reviews.AddAsync(review, cancellation);
                }
            }

            await _context.SaveChangesAsync(cancellation);
        }

        public async Task<Review> GetReviewAsync(int id)
        {
            return await _context.Reviews.FirstOrDefaultAsync(r => r.Id == id);
        }

        public async Task<IList<Review>> GetReviewsAsync()
        {
            return await _context.Reviews.ToListAsync();
        }

        public Review GetNewestReview()
        {
            return _context.Reviews.OrderByDescending(x => x.ReviewDate).ThenByDescending(x => x.ReviewTime).ThenBy(x => x.Id).FirstOrDefault();
        }

        public async Task<Review> GetReviewByUniqueKey(DateOnly date, TimeOnly time, string reviewName)
        {
            return await _context.Reviews.FirstOrDefaultAsync(x => x.ReviewDate == date && x.Name == reviewName && x.ReviewTime == time);
        }

        public void AddReviews(IList<Review> reviews)
        {
            var ids = reviews.Select(r => r.Id).ToList();

            var existing = _context.Reviews
                                         .Where(r => ids.Contains(r.Id))
                                         .ToDictionary(r => r.Id);

            foreach (var review in reviews)
            {
                if (existing.TryGetValue(review.Id, out var tracked))
                {
                    _context.Entry(tracked).CurrentValues.SetValues(review);
                }
                else
                {
                    _context.Reviews.Add(review);
                }
            }

            _context.SaveChanges();
        }
    }
}
