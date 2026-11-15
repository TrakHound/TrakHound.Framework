create or replace view "trakhound"."package_latest_view" as

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
from "trakhound"."package" as "package"
inner join (select "organization", "id", max("build_date") as "build_date" from "trakhound"."package" group by "organization", "id") as "latest" on "package"."organization" = "latest"."organization" and "package"."id" = "latest"."id" and "package"."build_date" = "latest"."build_date";
