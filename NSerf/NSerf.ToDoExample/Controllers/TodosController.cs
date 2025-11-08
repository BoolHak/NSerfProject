using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NSerf.ToDoExample.Data;
using NSerf.ToDoExample.Models;

namespace NSerf.ToDoExample.Controllers;

[ApiController]
[Route("api/[controller]")]
public class TodosController : ControllerBase
{
    private readonly TodoDbContext _context;
    private readonly ILogger<TodosController> _logger;

    public TodosController(TodoDbContext context, ILogger<TodosController> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <summary>
    /// Get all todos
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<Todo>>> GetTodos([FromQuery] bool? completed = null)
    {
        var query = _context.Todos.AsQueryable();

        if (completed.HasValue)
        {
            query = query.Where(t => t.IsCompleted == completed.Value);
        }

        var todos = await query.OrderByDescending(t => t.CreatedAt).ToListAsync();
        return Ok(todos);
    }

    /// <summary>
    /// Get a specific todo by ID
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<Todo>> GetTodo(int id)
    {
        var todo = await _context.Todos.FindAsync(id);

        if (todo == null)
        {
            return NotFound();
        }

        return Ok(todo);
    }

    /// <summary>
    /// Create a new todo
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<Todo>> CreateTodo([FromBody] CreateTodoRequest request)
    {
        var todo = new Todo
        {
            Title = request.Title,
            Description = request.Description,
            IsCompleted = false,
            CreatedAt = DateTime.UtcNow
        };

        _context.Todos.Add(todo);
        await _context.SaveChangesAsync();

        _logger.LogInformation("✅ Created todo {Id}: {Title}", todo.Id, todo.Title);

        return CreatedAtAction(nameof(GetTodo), new { id = todo.Id }, todo);
    }

    /// <summary>
    /// Update an existing todo
    /// </summary>
    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateTodo(int id, [FromBody] UpdateTodoRequest request)
    {
        var todo = await _context.Todos.FindAsync(id);

        if (todo == null)
        {
            return NotFound();
        }

        if (!string.IsNullOrEmpty(request.Title))
        {
            todo.Title = request.Title;
        }

        if (request.Description != null)
        {
            todo.Description = request.Description;
        }

        if (request.IsCompleted.HasValue && request.IsCompleted.Value != todo.IsCompleted)
        {
            todo.IsCompleted = request.IsCompleted.Value;
            todo.CompletedAt = request.IsCompleted.Value ? DateTime.UtcNow : null;
        }

        await _context.SaveChangesAsync();

        _logger.LogInformation("✅ Updated todo {Id}", id);

        return NoContent();
    }

    /// <summary>
    /// Delete a todo
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteTodo(int id)
    {
        var todo = await _context.Todos.FindAsync(id);

        if (todo == null)
        {
            return NotFound();
        }

        _context.Todos.Remove(todo);
        await _context.SaveChangesAsync();

        _logger.LogInformation("✅ Deleted todo {Id}", id);

        return NoContent();
    }
}

public record CreateTodoRequest(string Title, string? Description);
public record UpdateTodoRequest(string? Title, string? Description, bool? IsCompleted);
