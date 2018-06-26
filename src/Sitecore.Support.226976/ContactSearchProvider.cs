namespace Sitecore.Support.Cintel
{
  using Sitecore.Analytics.Model;
  using Sitecore.Cintel.Commons;
  using Sitecore.Cintel.Configuration;
  using Sitecore.Cintel.Reporting.Utility;
  using Sitecore.Cintel.Search;
  using Sitecore.ContentSearch;
  using Sitecore.ContentSearch.Analytics.Models;
  using Sitecore.ContentSearch.Linq;
  using Sitecore.ContentSearch.Security;
  using System;
  using System.Collections.Generic;
  using System.Linq;
  using Sitecore.Cintel;

  public class ContactSearchProvider : IContactSearchProvider
  {
    private IContactSearchResult BuildBaseResult(IndexedContact indexedContact)
    {
      ContactIdentificationLevel none;
      if (!Enum.TryParse<ContactIdentificationLevel>(indexedContact.IdentificationLevel, true, out none))
      {
        none = ContactIdentificationLevel.None;
      }
      return new ContactSearchResult
      {
        IdentificationLevel = (int)none,
        ContactId = indexedContact.ContactId,
        FirstName = indexedContact.FirstName,
        MiddleName = indexedContact.MiddleName,
        Surname = indexedContact.Surname,
        PreferredEmail = indexedContact.PreferredEmail,
        JobTitle = indexedContact.JobTitle,
        Value = indexedContact.Value,
        VisitCount = indexedContact.VisitCount,
        ValuePerVisit = Calculator.GetAverageValue((double)indexedContact.Value, (double)indexedContact.VisitCount)
      };
    }

    public ResultSet<List<IContactSearchResult>> Find(ContactSearchParameters parameters)
    {
      ResultSet<List<IContactSearchResult>> set = new ResultSet<List<IContactSearchResult>>(parameters.PageNumber, parameters.PageSize);
      ISearchIndex index = ContentSearchManager.GetIndex(CustomerIntelligenceConfig.ContactSearch.SearchIndexName);
      Func<IndexedContact, IContactSearchResult> selector = null;
      using (IProviderSearchContext ctx = index.CreateSearchContext(SearchSecurityOptions.Default))
      {
        //patch code starts
        var exactResults = QueryIndexExactMatch(ctx, parameters);
        SearchResults<IndexedContact> results = exactResults.TotalSearchResults > 0 ? exactResults : this.QueryIndex(ctx, parameters);
        //patch code ends

        List<IndexedContact> source = (from h in results.Hits select h.Document).ToList<IndexedContact>();
        set.TotalResultCount = results.TotalSearchResults;
        if (selector == null)
        {
          selector = delegate (IndexedContact sr)
          {
            IContactSearchResult contact = this.BuildBaseResult(sr);
            IndexedVisit visit = (from iv in ctx.GetQueryable<IndexedVisit>()
                                  where iv.ContactId == contact.ContactId
                                  orderby iv.StartDateTime descending
                                  select iv).Take<IndexedVisit>(1).FirstOrDefault<IndexedVisit>();
            if (visit != null)
            {
              this.PopulateLatestVisit(visit, ref contact);
            }
            return contact;
          };
        }
        List<IContactSearchResult> list2 = (from c in source.Select<IndexedContact, IContactSearchResult>(selector)
                                            orderby c.FirstName, c.LatestVisitStartDateTime
                                            select c).ToList<IContactSearchResult>();
        set.Data.Dataset.Add("ContactSearchResults", list2);
      }
      return set;
    }

    private void PopulateLatestVisit(IndexedVisit visit, ref IContactSearchResult contact)
    {
      contact.LatestVisitId = visit.InteractionId;
      contact.LatestVisitStartDateTime = visit.StartDateTime;
      contact.LatestVisitEndDateTime = visit.EndDateTime;
      contact.LatestVisitPageViewCount = visit.VisitPageCount;
      contact.LatestVisitValue = visit.Value;
      if (visit.WhoIs != null)
      {
        contact.LatestVisitLocationCityDisplayName = visit.WhoIs.City;
        contact.LatestVisitLocationCountryDisplayName = visit.WhoIs.Country;
        contact.LatestVisitLocationRegionDisplayName = visit.WhoIs.Region;
        contact.LatestVisitLocationId = new Guid?(visit.LocationId);
      }
    }

    private SearchResults<IndexedContact> QueryIndex(IProviderSearchContext ctx, ContactSearchParameters parameters)
    {
      IQueryable<IndexedContact> source = ctx.GetQueryable<IndexedContact>();
      string text = parameters.Match;
      if (string.IsNullOrEmpty(text.Trim()) || (text == "*"))
      {
        return source.Page<IndexedContact>((parameters.PageNumber - 1), parameters.PageSize).GetResults<IndexedContact>();
      }
      string wildcard = "*" + text + "*";
      int slop = 10;
      IQueryable<IndexedContact> queryable2 = from q in source
                                              where q.FullName.MatchWildcard<string>(wildcard) || q.Emails.MatchWildcard<List<string>>(wildcard)
                                              select q;
      if (!queryable2.Any<IndexedContact>())
      {
        queryable2 = from q in source
                     where q.FullName.Like<string>(text, slop) || q.Emails.Like<List<string>>(text, slop)
                     select q;
      }
      return queryable2.Page<IndexedContact>((parameters.PageNumber - 1), parameters.PageSize).GetResults<IndexedContact>();
    }

    private SearchResults<IndexedContact> QueryIndexExactMatch(IProviderSearchContext ctx, ContactSearchParameters parameters)
    {
      IQueryable<IndexedContact> source = ctx.GetQueryable<IndexedContact>();
      string text = parameters.Match;
      if (string.IsNullOrEmpty(text.Trim()) || (text == "*"))
      {
        return source.Page<IndexedContact>((parameters.PageNumber - 1), parameters.PageSize).GetResults<IndexedContact>();
      }

      var queryable2 = from q in source
                       where q.FullName.Like<string>(text, 0) || q.Emails.Like<List<string>>(text, 0)
                       select q;

      return queryable2.Page<IndexedContact>((parameters.PageNumber - 1), parameters.PageSize).GetResults<IndexedContact>();
    }
  }
}
