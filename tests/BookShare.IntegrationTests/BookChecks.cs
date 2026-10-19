using System.Net.Http.Headers;
using System.Net.Http.Json;
using BookShare.Application.DTOs;
using BookShare.Application.Interfaces;
using BookShare.Domain.Entities;
using BookShare.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;

public static class BookChecks
{
    public static async Task RunAsync(HttpClient client, IServiceProvider services, User owner)
    {
        int checks = 0;
        void Check(bool ok, string message) { if (!ok) throw new Exception(message); checks++; }
        async Task Status(HttpResponseMessage response, int expected)
        { using (response) Check((int)response.StatusCode == expected, $"Expected {expected}, received {response.StatusCode}: {await response.Content.ReadAsStringAsync()}"); }
        var original = client.DefaultRequestHeaders.Authorization;
        var users = services.GetRequiredService<IUserRepository>();
        var other = new User { Email = "other@example.com" }; users.Add(other);
        var admin = new User { Role = UserRole.Admin }; users.Add(admin);
        void As(User user)
        {
            using var scope = services.CreateScope();
            client.DefaultRequestHeaders.Authorization = new("Bearer", scope.ServiceProvider.GetRequiredService<ITokenIssuer>().Issue(user).AccessToken);
        }
        var categoryInput = new CategoryRequest { Name = " Fiction " };
        await Status(await client.PostAsJsonAsync("/api/categories", categoryInput), 403);
        As(admin);
        using var createdCategory = await client.PostAsJsonAsync("/api/categories", categoryInput);
        Check((int)createdCategory.StatusCode == 201 && createdCategory.Headers.Location is not null, "Category creation failed.");
        var category = (await createdCategory.Content.ReadFromJsonAsync<CategoryResponse>())!;
        Check(category.Name == "Fiction", "Category trim failed.");
        await Status(await client.PostAsJsonAsync("/api/categories", new { Name = "fiction" }), 409);
        As(owner);
        var input = new CreateBookRequest { Title = "Title", Author = "Author", Description = "Description", City = "Haifa", CategoryIds = [category.Id] };
        using var created = await client.PostAsJsonAsync("/api/books", input);
        Check((int)created.StatusCode == 201 && created.Headers.Location is not null, "Book creation failed.");
        var book = (await created.Content.ReadFromJsonAsync<BookResponse>())!;
        Check(book.OwnerId == owner.Id && book.Status == BookStatus.Available && book.Categories.Count == 1, "Creation invariants failed.");
        Check(!(await created.Content.ReadAsStringAsync()).Contains(owner.Email), "Contact leaked.");
        var update = new UpdateBookRequest { Version = book.Version, Title = input.Title, Author = input.Author, Description = input.Description, City = input.City, CategoryIds = input.CategoryIds, Status = BookStatus.Hidden };
        As(other);
        await Status(await client.PutAsJsonAsync($"/api/books/{book.Id}", update), 403);
        await Status(await client.DeleteAsync($"/api/books/{book.Id}"), 403);
        As(owner);
        await Status(await client.PutAsJsonAsync($"/api/books/{book.Id}", update), 200);
        client.DefaultRequestHeaders.Authorization = null;
        await Status(await client.GetAsync($"/api/books/{book.Id}"), 404);
        var publicHidden = (await client.GetFromJsonAsync<PagedResult<BookResponse>>("/api/books"))!;
        Check(publicHidden.TotalCount == 0, "Hidden leaked into public catalog.");
        As(admin);
        await Status(await client.GetAsync($"/api/books/{book.Id}"), 200);
        As(other);
        await Status(await client.GetAsync($"/api/books/{book.Id}"), 404);
        await Status(await client.GetAsync($"/api/books/{book.Id}/contact"), 404);
        var others = (await client.GetFromJsonAsync<PagedResult<BookResponse>>("/api/books?mine=true"))!;
        Check(others.TotalCount == 0, "Mine exposed another owner's books.");
        As(owner);
        var mine = (await client.GetFromJsonAsync<PagedResult<BookResponse>>("/api/books?mine=true"))!;
        Check(mine.Items.Single().Id == book.Id, "Mine omitted hidden book.");
        await Status(await client.GetAsync($"/api/books/{book.Id}/contact"), 200);
        update.Version = (await client.GetFromJsonAsync<BookResponse>($"/api/books/{book.Id}"))!.Version;
        update.Status = BookStatus.Loaned;
        await Status(await client.PutAsJsonAsync($"/api/books/{book.Id}", update), 400);
        update.Status = BookStatus.Available;
        await Status(await client.PutAsJsonAsync($"/api/books/{book.Id}", update), 200);
        input.CategoryIds = [Guid.NewGuid()];
        await Status(await client.PostAsJsonAsync("/api/books", input), 400);
        input.CategoryIds = [category.Id, category.Id];
        await Status(await client.PostAsJsonAsync("/api/books", input), 400);
        input.CategoryIds = [category.Id];
        for (int i = 0; i < 12; i++) await Status(await client.PostAsJsonAsync("/api/books", input), 201);
        var first = (await client.GetFromJsonAsync<PagedResult<BookResponse>>("/api/books?sortBy=title"))!;
        var second = (await client.GetFromJsonAsync<PagedResult<BookResponse>>("/api/books?sortBy=title&pageNumber=2"))!;
        Check(first.TotalCount == 13 && first.Items.Count == 10 && second.Items.Count == 3 && !first.Items.Select(x => x.Id).Intersect(second.Items.Select(x => x.Id)).Any(), "Paging failed.");
        foreach (var query in new[] { "search=missing", "city=missing", $"categoryId={Guid.NewGuid()}" })
        {
            var filtered = (await client.GetFromJsonAsync<PagedResult<BookResponse>>("/api/books?" + query))!;
            Check(filtered.TotalCount == 0, "Filter did not restrict catalog.");
        }
        var matches = (await client.GetFromJsonAsync<PagedResult<BookResponse>>($"/api/books?search=title&city=haifa&categoryId={category.Id}&availableOnly=true&pageSize=50"))!;
        Check(matches.TotalCount == 13 && matches.Items.Count == 13, "Combined filters failed.");
        var emptyPage = (await client.GetFromJsonAsync<PagedResult<BookResponse>>("/api/books?pageNumber=100"))!;
        Check(emptyPage.TotalCount == 13 && emptyPage.Items.Count == 0, "Empty page lost total count.");
        var storedBook = (await services.GetRequiredService<IBookRepository>().FindByIdAsync(book.Id, true, default))!;
        storedBook.Status = BookStatus.Loaned;
        await Status(await client.PutAsJsonAsync($"/api/books/{book.Id}", update), 409);
        await Status(await client.DeleteAsync($"/api/books/{book.Id}"), 409);
        var available = (await client.GetFromJsonAsync<PagedResult<BookResponse>>("/api/books?availableOnly=true"))!;
        Check(available.TotalCount == 12, "Loaned book passed availability filter.");
        storedBook.Status = BookStatus.Available;
        await Status(await client.GetAsync("/api/books?pageSize=51"), 400);
        await Status(await client.GetAsync("/api/books?pageNumber=0"), 400);
        await Status(await client.GetAsync("/api/books?sortBy=invalid"), 400);
        As(admin);
        await Status(await client.PutAsJsonAsync($"/api/categories/{category.Id}", new { Name = " Fiction " }), 200);
        await Status(await client.GetAsync($"/api/categories/{category.Id}"), 200);
        using var unusedResponse = await client.PostAsJsonAsync("/api/categories", new { Name = "Unused" });
        var unused = (await unusedResponse.Content.ReadFromJsonAsync<CategoryResponse>())!;
        await Status(await client.PutAsJsonAsync($"/api/categories/{unused.Id}", new { Name = "fiction" }), 409);
        await Status(await client.DeleteAsync($"/api/categories/{unused.Id}"), 204);
        await Status(await client.GetAsync($"/api/categories/{unused.Id}"), 404);
        await Status(await client.DeleteAsync($"/api/categories/{category.Id}"), 409);
        update.Version = (await client.GetFromJsonAsync<BookResponse>($"/api/books/{book.Id}"))!.Version;
        await Status(await client.PutAsJsonAsync($"/api/books/{book.Id}", update), 200);
        await Status(await client.DeleteAsync($"/api/books/{book.Id}"), 204);
        client.DefaultRequestHeaders.Authorization = null;
        await Status(await client.GetAsync("/api/books?mine=true"), 401);
        await Status(await client.GetAsync($"/api/books/{first.Items.Last().Id}/contact"), 401);
        await Status(await client.PostAsJsonAsync("/api/books", input), 401);
        client.DefaultRequestHeaders.Authorization = original;
        Console.WriteLine($"Passed {checks} book/category HTTP checks with production services and JWT; memory repositories, no PostgreSQL.");
    }
}

public sealed class MemoryBooks : IBookRepository
{
    public List<Book> Items { get; } = [];
    public void Add(Book book) => Items.Add(book);
    public void Remove(Book book) => Items.Remove(book);
    public Task<Book?> FindByIdAsync(Guid id, bool trackChanges, CancellationToken ct) => Task.FromResult(Items.SingleOrDefault(x => x.Id == id));
    public Task<PagedResult<Book>> ListAsync(BookQuery query, Guid? ownerId, CancellationToken ct)
    {
        IEnumerable<Book> items = Items.Where(x => query.ReservedByMe
            ? x.Status == BookStatus.Reserved && x.ReservedById == ownerId
            : ownerId.HasValue ? x.OwnerId == ownerId : x.Status != BookStatus.Hidden);
        if (query.AvailableOnly) items = items.Where(x => x.Status == BookStatus.Available);
        if (query.CategoryId.HasValue) items = items.Where(x => x.BookCategories.Any(c => c.CategoryId == query.CategoryId));
        if (!string.IsNullOrWhiteSpace(query.City)) items = items.Where(x => x.City.Equals(query.City.Trim(), StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(query.Search)) items = items.Where(x => (x.Title + x.Author + x.Description).Contains(query.Search.Trim(), StringComparison.OrdinalIgnoreCase));
        var sorted = query.SortBy switch { "title" => items.OrderBy(x => x.Title).ThenBy(x => x.Id), "title_desc" => items.OrderByDescending(x => x.Title).ThenBy(x => x.Id), "date" => items.OrderBy(x => x.CreatedAtUtc).ThenBy(x => x.Id), _ => items.OrderByDescending(x => x.CreatedAtUtc).ThenBy(x => x.Id) };
        return Task.FromResult(new PagedResult<Book>(sorted.Skip((query.PageNumber - 1) * query.PageSize).Take(query.PageSize).ToList(), items.Count(), query.PageNumber, query.PageSize));
    }
}
public sealed class MemoryCategories(IBookRepository books) : ICategoryRepository
{
    private readonly List<Category> items = [];
    public void Add(Category category) => items.Add(category);
    public void Remove(Category category) => items.Remove(category);
    public Task<IReadOnlyList<Category>> ListAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<Category>>(items.ToList());
    public Task<Category?> FindByIdAsync(Guid id, CancellationToken ct) => Task.FromResult(items.SingleOrDefault(x => x.Id == id));
    public Task<Category?> FindByNormalizedNameAsync(string name, CancellationToken ct) => Task.FromResult(items.SingleOrDefault(x => x.NormalizedName == name));
    public Task<IReadOnlyList<Category>> FindByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) => Task.FromResult<IReadOnlyList<Category>>(items.Where(x => ids.Contains(x.Id)).ToList());
    public Task<bool> IsAssignedAsync(Guid id, CancellationToken ct) => Task.FromResult(((MemoryBooks)books).Items.Any(x => x.BookCategories.Any(c => c.CategoryId == id)));
}
