// Adapt these functions to the current site after its source is provided.
export async function redeemOptionalBetaCode(supabase, code) {
  if (!code?.trim()) return { beta: false };
  const { data, error } = await supabase.rpc('redeem_beta_code', { p_code: code.trim() });
  if (error) throw error;
  if (!data) throw new Error('Код недействителен, исчерпан или истёк.');
  return { beta: true };
}
export async function loadAccountRole(supabase) {
  const { data, error } = await supabase.rpc('get_my_beta_access');
  if (error) throw error;
  return data; // role/is_admin/has_beta/beta_status
}
export async function downloadBeta(supabase) {
  const { data, error } = await supabase.functions.invoke('download-beta');
  if (error) throw error;
  window.location.assign(data.downloadUrl);
}

// The browser writes the same public URL to Auth metadata and public.profiles.
// Query the profile first so an already-open website does not reuse stale local session metadata.
export async function loadLumaAvatarUrl(supabase) {
  const { data: { session }, error: sessionError } = await supabase.auth.getSession();
  if (sessionError) throw sessionError;
  if (!session?.user?.id) return null;
  const { data, error } = await supabase
    .from('profiles')
    .select('avatar_url')
    .eq('id', session.user.id)
    .maybeSingle();
  if (error) throw error;
  return data?.avatar_url ?? session.user.user_metadata?.avatar_url ?? null;
}
