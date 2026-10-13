-- 010_nanobanana_image.sql
-- Configuration table and storage bucket for Nano Banana AI image generation

CREATE TABLE IF NOT EXISTS public.app_config (
    key text PRIMARY KEY,
    value text NOT NULL DEFAULT '',
    description text,
    created_at timestamptz DEFAULT now(),
    updated_at timestamptz DEFAULT now()
);

-- Enable Row Level Security
ALTER TABLE public.app_config ENABLE ROW LEVEL SECURITY;

-- Service role has full unrestricted access
CREATE POLICY "Service role full access to app_config"
    ON public.app_config
    FOR ALL
    TO service_role
    USING (true)
    WITH CHECK (true);

-- Authenticated users (and anon) can read configurations
CREATE POLICY "Public read app_config"
    ON public.app_config
    FOR SELECT
    TO public
    USING (true);

-- Insert defaults for Nano Banana / cun.ai
INSERT INTO public.app_config (key, value, description)
VALUES 
    ('nanobanana_api_key', 'YOUR_API_KEY_HERE', 'API ключ Nano Banana / cun.ai для генерации картинок в LumaAI'),
    ('nanobanana_endpoint', 'https://www.cun.ai', 'API Endpoint: https://www.cun.ai или https://api.kie.ai/api/v1/jobs/createTask'),
    ('nanobanana_model', 'gemini-3.1-flash-image-preview', 'Модель: gemini-3.1-flash-image-preview или nano-banana-2')
ON CONFLICT (key) DO UPDATE SET value = EXCLUDED.value, description = EXCLUDED.description;

-- Storage bucket for reference images and generated media
INSERT INTO storage.buckets (id, name, public, file_size_limit, allowed_mime_types)
VALUES ('assistant-images', 'assistant-images', true, 10485760, array['image/png', 'image/jpeg', 'image/webp', 'image/gif'])
ON CONFLICT (id) DO UPDATE SET public = true;

CREATE POLICY "Public read assistant-images"
    ON storage.objects FOR SELECT
    TO public
    USING (bucket_id = 'assistant-images');

CREATE POLICY "Public insert assistant-images"
    ON storage.objects FOR INSERT
    TO public
    WITH CHECK (bucket_id = 'assistant-images');

CREATE POLICY "Service role full access assistant-images"
    ON storage.objects FOR ALL
    TO service_role
    USING (bucket_id = 'assistant-images')
    WITH CHECK (bucket_id = 'assistant-images');

-- Daily usage tracking for image generation (2 images/day limit for non-unlimited users)
CREATE TABLE IF NOT EXISTS public.assistant_image_usage (
    user_id uuid NOT NULL REFERENCES auth.users(id) ON DELETE CASCADE,
    usage_date date NOT NULL DEFAULT (now() AT TIME ZONE 'utc')::date,
    image_count integer NOT NULL DEFAULT 0 CHECK (image_count >= 0),
    updated_at timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (user_id, usage_date)
);

ALTER TABLE public.assistant_image_usage ENABLE ROW LEVEL SECURITY;

CREATE POLICY "Users can view own image usage"
    ON public.assistant_image_usage FOR SELECT
    TO authenticated
    USING (auth.uid() = user_id);

CREATE POLICY "Service role full access assistant_image_usage"
    ON public.assistant_image_usage FOR ALL
    TO service_role
    USING (true)
    WITH CHECK (true);

-- Function to consume daily image quota
CREATE OR REPLACE FUNCTION public.consume_image_quota()
RETURNS jsonb
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = public
AS $$
DECLARE
  v_user uuid := auth.uid();
  v_email text;
  v_date date := (now() at time zone 'utc')::date;
  v_used integer;
  v_limit constant integer := 2;
BEGIN
  IF v_user IS NULL THEN
    RAISE EXCEPTION 'Authentication required' USING errcode = '42501';
  END IF;

  SELECT lower(email) INTO v_email FROM auth.users WHERE id = v_user;
  IF v_email = 'dakowerr@gmail.com' THEN
    RETURN jsonb_build_object('allowed', true, 'unlimited', true, 'limit', 0, 'used', 0, 'remaining', -1, 'usage_date', v_date);
  END IF;

  INSERT INTO public.assistant_image_usage(user_id, usage_date, image_count, updated_at)
  VALUES(v_user, v_date, 1, now())
  ON CONFLICT(user_id, usage_date) DO UPDATE
    SET image_count = public.assistant_image_usage.image_count + 1,
        updated_at = now()
    WHERE public.assistant_image_usage.image_count < v_limit
  RETURNING image_count INTO v_used;

  IF v_used IS NULL THEN
    SELECT image_count INTO v_used
    FROM public.assistant_image_usage
    WHERE user_id = v_user AND usage_date = v_date;
    RETURN jsonb_build_object('allowed', false, 'unlimited', false, 'limit', v_limit, 'used', coalesce(v_used, v_limit), 'remaining', 0, 'usage_date', v_date);
  END IF;

  RETURN jsonb_build_object('allowed', true, 'unlimited', false, 'limit', v_limit, 'used', v_used, 'remaining', greatest(v_limit - v_used, 0), 'usage_date', v_date);
END;
$$;

REVOKE ALL ON FUNCTION public.consume_image_quota() FROM public;
GRANT EXECUTE ON FUNCTION public.consume_image_quota() TO authenticated;
