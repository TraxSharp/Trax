// A crate's public surface, read from its source on the stable toolchain: what its root re-exports, each item as
// written with its docs and private parts left out, and the impls of those items. Two tests read it: each crate's
// `public_api.rs`, which compares it with a committed snapshot, and the client's `client_surface.rs`, which holds the
// client to the table in `conformance/client/PROTOCOL.md`.
//
// It reads the layout these crates use and refuses anything else rather than guess: a root of `mod` declarations,
// `pub use module::{..}` re-exports and public items of its own, one file per module. An item a `macro_rules!` writes
// is read from the macro's body with its repetitions dropped, so `RefusalCode` is listed without its variants (the
// registry test in `tests/engine.rs` holds those).

#![allow(dead_code)]

use std::collections::{BTreeMap, BTreeSet};
use std::path::{Path, PathBuf};

use proc_macro2::{Delimiter, Group, TokenStream, TokenTree};
use syn::{Attribute, Fields, ImplItem, Item, TraitItem, Type, UseTree, Visibility};

/// What one crate's root exports.
pub struct Surface {
    /// Each exported name, with its item.
    pub items: BTreeMap<String, Item>,
    /// The impls of the exported items, by the name of the type they are for, in source order.
    pub impls: BTreeMap<String, Vec<syn::ItemImpl>>,
}

impl Surface {
    /// Reads the crate whose manifest is in `crate_dir`.
    pub fn read(crate_dir: &Path) -> Surface {
        let src = crate_dir.join("src");
        let root = parse(&src.join("lib.rs"));
        let mut modules: Vec<(String, Vec<Item>)> = vec![(String::new(), with_macro_items(root.items.clone()))];
        for item in &root.items {
            if let Item::Mod(m) = item {
                if m.content.is_none() && !is_test(&m.attrs) {
                    let name = m.ident.to_string();
                    let file = parse(&module_file(&src, &name));
                    modules.push((name, with_macro_items(file.items)));
                }
            }
        }

        let mut exports: Vec<(String, String)> = Vec::new();
        for item in &root.items {
            match item {
                Item::Use(u) if is_pub(&u.vis) => re_exports(&u.tree, None, &mut exports),
                Item::Use(_) | Item::Mod(_) => {}
                other => {
                    if let Some(name) = public_name(other) {
                        exports.push((name, String::new()));
                    }
                }
            }
        }

        let mut items = BTreeMap::new();
        for (name, module) in exports {
            let found = modules
                .iter()
                .find(|(m, _)| *m == module)
                .and_then(|(_, list)| list.iter().find(|i| item_name(i).as_deref() == Some(name.as_str())))
                .unwrap_or_else(|| panic!("{}: `{name}` is re-exported from `{module}`, but the reader cannot find it there; extend tests/support/surface.rs", crate_dir.display()));
            items.insert(name, found.clone());
        }

        let mut impls: BTreeMap<String, Vec<syn::ItemImpl>> = BTreeMap::new();
        for (_, list) in &modules {
            for item in list {
                if let Item::Impl(i) = item {
                    if let Some(name) = type_name(&i.self_ty) {
                        if items.contains_key(&name) {
                            impls.entry(name).or_default().push(i.clone());
                        }
                    }
                }
            }
        }
        Surface { items, impls }
    }

    /// The surface as text: each exported item, then its impls, by name.
    pub fn render(&self) -> String {
        let mut file = syn::File {
            shebang: None,
            attrs: Vec::new(),
            items: Vec::new(),
        };
        for (name, item) in &self.items {
            file.items.push(strip_item(item.clone()));
            for i in self.impls.get(name).into_iter().flatten() {
                if let Some(i) = strip_impl(i.clone()) {
                    file.items.push(Item::Impl(i));
                }
            }
        }
        prettyplease::unparse(&file)
    }

    /// The public members of the exported type `name`: a struct's public fields, its inherent impls' public functions
    /// and constants, and the functions of each trait of this crate it implements; a trait's own items. None when the
    /// crate exports no type of that name.
    pub fn members(&self, name: &str) -> Option<BTreeSet<String>> {
        let mut members = BTreeSet::new();
        match self.items.get(name)? {
            Item::Struct(s) => {
                if let Fields::Named(fields) = &s.fields {
                    for f in &fields.named {
                        if is_pub(&f.vis) {
                            members.insert(f.ident.as_ref().unwrap().to_string());
                        }
                    }
                }
            }
            Item::Trait(t) => {
                for item in &t.items {
                    match item {
                        TraitItem::Fn(f) => members.insert(f.sig.ident.to_string()),
                        TraitItem::Const(c) => members.insert(c.ident.to_string()),
                        TraitItem::Type(t) => members.insert(t.ident.to_string()),
                        _ => false,
                    };
                }
            }
            Item::Enum(_) | Item::Union(_) => {}
            _ => return None,
        }
        for i in self.impls.get(name).into_iter().flatten() {
            match &i.trait_ {
                None => {
                    for item in &i.items {
                        match item {
                            ImplItem::Fn(f) if is_pub(&f.vis) => members.insert(f.sig.ident.to_string()),
                            ImplItem::Const(c) if is_pub(&c.vis) => members.insert(c.ident.to_string()),
                            _ => false,
                        };
                    }
                }
                Some((_, path, _)) => {
                    let trait_name = path.segments.last().unwrap().ident.to_string();
                    if let Some(Item::Trait(t)) = self.items.get(&trait_name) {
                        for item in &t.items {
                            if let TraitItem::Fn(f) = item {
                                members.insert(f.sig.ident.to_string());
                            }
                        }
                    }
                }
            }
        }
        Some(members)
    }
}

/// Compares the surface of the crate in `crate_dir` with the snapshot at `snapshot` (relative to it), or writes the
/// snapshot when `TRAX_UPDATE_PUBLIC_API` is set.
pub fn check_snapshot(crate_dir: &str, snapshot: &str, crate_name: &str) {
    let crate_dir = PathBuf::from(crate_dir);
    let current = format!(
        "// The public surface of {crate_name}, as tests/support/surface.rs reads it from the source. A test compares\n\
         // it; after a deliberate change, run `TRAX_UPDATE_PUBLIC_API=1 cargo test --workspace` and commit the diff.\n\n{}",
        Surface::read(&crate_dir).render()
    );
    let path = crate_dir.join(snapshot);
    if std::env::var_os("TRAX_UPDATE_PUBLIC_API").is_some() {
        std::fs::write(&path, &current).unwrap_or_else(|e| panic!("{}: {e}", path.display()));
        return;
    }
    let committed = std::fs::read_to_string(&path).unwrap_or_default().replace("\r\n", "\n");
    if committed == current {
        return;
    }
    let committed_lines: Vec<&str> = committed.lines().collect();
    let current_lines: Vec<&str> = current.lines().collect();
    let at = committed_lines
        .iter()
        .zip(&current_lines)
        .position(|(a, b)| a != b)
        .unwrap_or(committed_lines.len().min(current_lines.len()));
    panic!(
        "The public surface of {crate_name} is not the one committed in {snapshot}. Adding, removing or changing a \
         public item is an API change: if it is deliberate, run `TRAX_UPDATE_PUBLIC_API=1 cargo test --workspace` and \
         commit the snapshot with it.\nFirst difference, line {}:\n  committed: {}\n  now:       {}",
        at + 1,
        committed_lines.get(at).copied().unwrap_or("<end of file>"),
        current_lines.get(at).copied().unwrap_or("<end of file>"),
    );
}

fn parse(path: &Path) -> syn::File {
    let text = std::fs::read_to_string(path).unwrap_or_else(|e| panic!("{}: {e}", path.display()));
    syn::parse_file(&text).unwrap_or_else(|e| panic!("{}: {e}", path.display()))
}

fn module_file(src: &Path, name: &str) -> PathBuf {
    let flat = src.join(format!("{name}.rs"));
    if flat.exists() {
        flat
    } else {
        src.join(name).join("mod.rs")
    }
}

fn is_pub(vis: &Visibility) -> bool {
    matches!(vis, Visibility::Public(_))
}

fn is_test(attrs: &[Attribute]) -> bool {
    attrs.iter().any(|a| {
        a.path().is_ident("cfg")
            && a.meta
                .require_list()
                .is_ok_and(|l| l.tokens.to_string().contains("test"))
    })
}

fn re_exports(tree: &UseTree, module: Option<&str>, out: &mut Vec<(String, String)>) {
    match tree {
        UseTree::Path(p) if module.is_none() => re_exports(&p.tree, Some(&p.ident.to_string()), out),
        UseTree::Name(n) if module.is_some() => out.push((n.ident.to_string(), module.unwrap().to_string())),
        UseTree::Group(g) if module.is_some() => {
            for t in &g.items {
                re_exports(t, module, out);
            }
        }
        _ => {
            panic!("a root re-export the reader does not handle: only `pub use module::{{..}}` of plain names is read")
        }
    }
}

fn public_name(item: &Item) -> Option<String> {
    let public = match item {
        Item::Const(i) => is_pub(&i.vis),
        Item::Enum(i) => is_pub(&i.vis),
        Item::Fn(i) => is_pub(&i.vis),
        Item::Mod(i) => is_pub(&i.vis),
        Item::Static(i) => is_pub(&i.vis),
        Item::Struct(i) => is_pub(&i.vis),
        Item::Trait(i) => is_pub(&i.vis),
        Item::Type(i) => is_pub(&i.vis),
        Item::Union(i) => is_pub(&i.vis),
        _ => false,
    };
    if public {
        item_name(item)
    } else {
        None
    }
}

fn item_name(item: &Item) -> Option<String> {
    let ident = match item {
        Item::Const(i) => &i.ident,
        Item::Enum(i) => &i.ident,
        Item::Fn(i) => &i.sig.ident,
        Item::Mod(i) => &i.ident,
        Item::Static(i) => &i.ident,
        Item::Struct(i) => &i.ident,
        Item::Trait(i) => &i.ident,
        Item::Type(i) => &i.ident,
        Item::Union(i) => &i.ident,
        _ => return None,
    };
    Some(ident.to_string())
}

fn type_name(ty: &Type) -> Option<String> {
    match ty {
        Type::Path(p) => p.path.segments.last().map(|s| s.ident.to_string()),
        _ => None,
    }
}

/// The module's items, and the items each of its `macro_rules!` bodies writes, read with their repetitions dropped.
fn with_macro_items(mut items: Vec<Item>) -> Vec<Item> {
    let mut written = Vec::new();
    for item in &items {
        if let Item::Macro(m) = item {
            if m.mac.path.is_ident("macro_rules") {
                for body in rule_bodies(m.mac.tokens.clone()) {
                    if let Some(tokens) = drop_repetitions(body) {
                        if let Ok(file) = syn::parse2::<syn::File>(tokens) {
                            written.extend(file.items);
                        }
                    }
                }
            }
        }
    }
    items.extend(written);
    items
}

/// The transcribers of a `macro_rules!`: `(matcher) => { body };`, repeated.
fn rule_bodies(tokens: TokenStream) -> Vec<TokenStream> {
    let trees: Vec<TokenTree> = tokens.into_iter().collect();
    let mut bodies = Vec::new();
    let mut i = 0;
    while i + 3 < trees.len() + 1 {
        if let (
            TokenTree::Group(_),
            Some(TokenTree::Punct(eq)),
            Some(TokenTree::Punct(gt)),
            Some(TokenTree::Group(b)),
        ) = (&trees[i], trees.get(i + 1), trees.get(i + 2), trees.get(i + 3))
        {
            if eq.as_char() == '=' && gt.as_char() == '>' {
                bodies.push(b.stream());
                i += 4;
                if matches!(trees.get(i), Some(TokenTree::Punct(p)) if p.as_char() == ';') {
                    i += 1;
                }
                continue;
            }
        }
        break;
    }
    bodies
}

/// The tokens with every `$( .. ) sep? op` repetition dropped; None when a metavariable stands outside one.
fn drop_repetitions(tokens: TokenStream) -> Option<TokenStream> {
    let mut out = Vec::new();
    let mut iter = tokens.into_iter().peekable();
    while let Some(tree) = iter.next() {
        match tree {
            TokenTree::Punct(p) if p.as_char() == '$' => match iter.next() {
                Some(TokenTree::Group(g)) if g.delimiter() == Delimiter::Parenthesis => {
                    let op = |t: &Option<TokenTree>| matches!(t, Some(TokenTree::Punct(p)) if matches!(p.as_char(), '*' | '+' | '?'));
                    let next = iter.next();
                    if !op(&next) && !op(&iter.next()) {
                        return None;
                    }
                }
                _ => return None,
            },
            TokenTree::Group(g) => {
                let mut group = Group::new(g.delimiter(), drop_repetitions(g.stream())?);
                group.set_span(g.span());
                out.push(TokenTree::Group(group));
            }
            other => out.push(other),
        }
    }
    Some(out.into_iter().collect())
}

/// Keeps the attributes that are part of an item's contract.
fn kept(attrs: Vec<Attribute>) -> Vec<Attribute> {
    attrs
        .into_iter()
        .filter(|a| a.path().is_ident("derive") || a.path().is_ident("non_exhaustive") || a.path().is_ident("repr"))
        .collect()
}

fn empty_body() -> syn::Block {
    syn::Block {
        brace_token: Default::default(),
        stmts: Vec::new(),
    }
}

fn strip_item(item: Item) -> Item {
    match item {
        Item::Struct(mut s) => {
            s.attrs = kept(s.attrs);
            match &mut s.fields {
                Fields::Named(f) => {
                    f.named = std::mem::take(&mut f.named)
                        .into_iter()
                        .filter(|f| is_pub(&f.vis))
                        .map(|mut f| {
                            f.attrs = kept(f.attrs);
                            f
                        })
                        .collect();
                }
                Fields::Unnamed(f) => {
                    for field in f.unnamed.iter_mut() {
                        field.attrs = kept(std::mem::take(&mut field.attrs));
                    }
                }
                Fields::Unit => {}
            }
            Item::Struct(s)
        }
        Item::Enum(mut e) => {
            e.attrs = kept(e.attrs);
            for v in e.variants.iter_mut() {
                v.attrs = kept(std::mem::take(&mut v.attrs));
                for f in v.fields.iter_mut() {
                    f.attrs = kept(std::mem::take(&mut f.attrs));
                }
            }
            Item::Enum(e)
        }
        Item::Trait(mut t) => {
            t.attrs = kept(t.attrs);
            for item in t.items.iter_mut() {
                match item {
                    TraitItem::Fn(f) => {
                        f.attrs = kept(std::mem::take(&mut f.attrs));
                        f.default = None;
                        f.semi_token = Some(Default::default());
                    }
                    TraitItem::Const(c) => c.attrs = kept(std::mem::take(&mut c.attrs)),
                    TraitItem::Type(t) => t.attrs = kept(std::mem::take(&mut t.attrs)),
                    _ => {}
                }
            }
            Item::Trait(t)
        }
        Item::Fn(mut f) => {
            f.attrs = kept(f.attrs);
            *f.block = empty_body();
            Item::Fn(f)
        }
        Item::Const(mut c) => {
            c.attrs = kept(c.attrs);
            Item::Const(c)
        }
        Item::Static(mut s) => {
            s.attrs = kept(s.attrs);
            Item::Static(s)
        }
        Item::Type(mut t) => {
            t.attrs = kept(t.attrs);
            Item::Type(t)
        }
        Item::Mod(mut m) => {
            m.attrs = kept(m.attrs);
            if let Some((_, items)) = &mut m.content {
                *items = std::mem::take(items)
                    .into_iter()
                    .filter(|i| public_name(i).is_some())
                    .map(strip_item)
                    .collect();
            }
            Item::Mod(m)
        }
        other => panic!(
            "an exported item the reader does not handle: {}",
            item_name(&other).unwrap_or_default()
        ),
    }
}

/// An impl with its public parts only: a trait impl as its header, an inherent impl as its public functions and
/// constants, or None when it has none.
fn strip_impl(mut i: syn::ItemImpl) -> Option<syn::ItemImpl> {
    i.attrs = kept(i.attrs);
    if i.trait_.is_some() {
        i.items.clear();
        return Some(i);
    }
    i.items = std::mem::take(&mut i.items)
        .into_iter()
        .filter_map(|item| match item {
            ImplItem::Fn(mut f) if is_pub(&f.vis) => {
                f.attrs = kept(f.attrs);
                f.block = empty_body();
                Some(ImplItem::Fn(f))
            }
            ImplItem::Const(mut c) if is_pub(&c.vis) => {
                c.attrs = kept(c.attrs);
                Some(ImplItem::Const(c))
            }
            _ => None,
        })
        .collect();
    if i.items.is_empty() {
        None
    } else {
        Some(i)
    }
}
