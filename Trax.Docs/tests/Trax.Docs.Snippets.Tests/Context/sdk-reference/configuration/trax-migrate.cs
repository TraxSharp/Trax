// The application's own context the trax-migrate page's example names with MigrateOnDeploy.
using Microsoft.EntityFrameworkCore;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options);
