using DAL.Data;
using DAL.Interfaces;
using DAL.Models;
using Microsoft.EntityFrameworkCore;

namespace DAL.Implementations;
public class AssistantDataDal : IAssistantDataDal
{
    private readonly OzonBotDbContext _db;
    public AssistantDataDal(OzonBotDbContext ozonBotDbContext)
    {
        _db = ozonBotDbContext;
    }

    public async Task AddOrUpdateAsync(AssistantData? dbObject)
    {
        if (dbObject == null)
        {
            return;
        }
        var exist = await _db.AssistantDatas.FirstOrDefaultAsync(x => x.Id == dbObject.Id);
        if (exist == null)
        {
            await _db.AssistantDatas.AddAsync(dbObject);
            await _db.SaveChangesAsync();
            return;
        }

        exist.AssistantName = dbObject.AssistantName;
        exist.AssistantId = dbObject.AssistantId;
        _db.AssistantDatas.Update(exist);
        await _db.SaveChangesAsync();
    }

    public async Task<AssistantData?> GetByIdAsync(int id)
    {
        var dbObject = await _db.AssistantDatas.FirstOrDefaultAsync(x => x.Id == id);
        if (dbObject == null)
        {
            return null;
        }
        return dbObject;
    }

    public async Task<AssistantData?> GetByAssistantIdAsync(string assistantId)
    {

        var dbObject = await _db.AssistantDatas.FirstOrDefaultAsync(x => x.AssistantId == assistantId);
        if (dbObject == null)
        {
            return null;
        }
        return dbObject;
    }

    public async Task<List<AssistantData>> GetAllAsync()
    {
        var dbObjects = await _db.AssistantDatas.ToListAsync();
        return dbObjects;
    }

    public async Task DeleteAsync(int id)
    {
        var assistantData = await _db.AssistantDatas.FirstOrDefaultAsync(x => x.Id == id);
        if (assistantData != null)
        {
            _db.AssistantDatas.Remove(assistantData);
            await _db.SaveChangesAsync();
        }
    }

    public AssistantData? FromEntityToDb(Entities.AssistantData? entity)
    {
        if (entity == null)
        {
            return null;
        }
        return new AssistantData
        {
            Id = entity.Id,
            AssistantName = entity.AssistantName,
            AssistantId = entity.AssistantId
        };
    }
    public Entities.AssistantData? FromDbToEntity(AssistantData? dbObject)
    {
        if (dbObject == null)
        {
            return null;
        }
        return new Entities.AssistantData(assistantName: dbObject.AssistantName, assistantId: dbObject.AssistantId)
        {
            Id = dbObject.Id
        };
    }
}
