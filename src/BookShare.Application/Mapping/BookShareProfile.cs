using AutoMapper;
using BookShare.Application.DTOs;
using BookShare.Domain.Entities;

namespace BookShare.Application.Mapping;

public sealed class BookShareProfile : Profile
{
    public BookShareProfile()
    {
        CreateMap<Loan, LoanResponse>();
        CreateMap<User, UserResponse>();
        CreateMap<User, BookContactResponse>();
        CreateMap<Category, CategoryResponse>();
        CreateMap<Book, BookResponse>()
            .ForMember(x => x.IsReservedByMe, options => options.Ignore())
            .ForCtorParam("Categories", options => options.MapFrom(
                source => source.BookCategories.Select(link => link.Category)));
    }
}
