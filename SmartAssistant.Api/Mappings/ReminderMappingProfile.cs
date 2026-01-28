using AutoMapper;
using SmartAssistant.Api.Models;
using SmartAssistant.Core.Entities;

namespace SmartAssistant.Api.Mappings
{
    public class ReminderMappingProfile : Profile
    {
        public ReminderMappingProfile()
        {
            CreateMap<ReminderViewModel, Reminder>().ReverseMap();
        }
    }
}
