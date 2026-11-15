drop view if exists "trakhound"."package_latest_view";

-- A SQLite view can only use objects in its own database, so its Table has no Schema
create view "trakhound"."package_latest_view" as

select
"package"."organization",
"package"."uuid",
"package"."id",
"package"."version_major",
"package"."version_minor",
"package"."version_patch",
"package"."category",
"package"."build_date",
"package"."hash"
from "package" as "package"
inner join (select "organization", "id", max("build_date") as "build_date" from "package" group by "organization", "id") as "latest" on "package"."organization" = "latest"."organization" and "package"."id" = "latest"."id" and "package"."build_date" = "latest"."build_date";
