using AutoMapper;
using OrderManagement.API.Data.Entities;
using OrderManagement.API.DTOs;

namespace OrderManagement.API.Profiles;

/// <summary>
/// AutoMapper profile: maps Order / OrderLine entities to their DTO counterparts.
/// The LineTotal projection (Quantity * UnitPrice) is computed here so the
/// entity model stays clean (no stored column).
/// </summary>
public class OrderMappingProfile : Profile
{
    public OrderMappingProfile()
    {
        // ── OrderLine → OrderLineResponse ────────────────────────────────────
        CreateMap<OrderLine, OrderLineResponse>()
            .ForMember(dest => dest.LineTotal,
                       opt => opt.MapFrom(src => src.Quantity * src.UnitPrice));

        // ── Order → OrderResponse ─────────────────────────────────────────────
        // All scalar properties map by convention (same name + type).
        // Lines collection is mapped via the OrderLine → OrderLineResponse map above.
        CreateMap<Order, OrderResponse>();
    }
}
