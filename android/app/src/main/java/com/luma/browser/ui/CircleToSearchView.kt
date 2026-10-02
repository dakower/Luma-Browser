package com.luma.browser.ui

import android.animation.ValueAnimator
import android.annotation.SuppressLint
import android.content.Context
import android.graphics.*
import android.util.AttributeSet
import android.view.MotionEvent
import android.view.View
import android.view.animation.DecelerateInterpolator

class CircleToSearchView @JvmOverloads constructor(
    context: Context,
    attrs: AttributeSet? = null,
    defStyleAttr: Int = 0
) : View(context, attrs, defStyleAttr) {

    var screenshotBitmap: Bitmap? = null
        set(value) {
            field = value
            invalidate()
        }

    var onSelectionComplete: ((Bitmap) -> Unit)? = null
    var onDismiss: (() -> Unit)? = null

    private val drawPath = Path()
    private val points = mutableListOf<PointF>()
    private var lastX = 0f
    private var lastY = 0f
    private var isDrawing = false
    private var isFlashActive = false
    private var flashAlpha = 0f

    // Animated Aura
    private var auraAngle = 0f
    private val auraAnimator: ValueAnimator = ValueAnimator.ofFloat(0f, 360f).apply {
        duration = 4000
        repeatCount = ValueAnimator.INFINITE
        repeatMode = ValueAnimator.RESTART
        addUpdateListener {
            auraAngle = it.animatedValue as Float
            invalidate()
        }
    }

    // Scrim overlay
    private val scrimPaint = Paint().apply {
        color = Color.parseColor("#4407060F")
    }

    // Outer glow for drawing lasso
    private val glowPaint = Paint(Paint.ANTI_ALIAS_FLAG).apply {
        style = Paint.Style.STROKE
        strokeWidth = 26f
        strokeCap = Paint.Cap.ROUND
        strokeJoin = Paint.Join.ROUND
        color = Color.parseColor("#80A78BFA")
        maskFilter = BlurMaskFilter(14f, BlurMaskFilter.Blur.NORMAL)
    }

    // Inner bright laser core
    private val corePaint = Paint(Paint.ANTI_ALIAS_FLAG).apply {
        style = Paint.Style.STROKE
        strokeWidth = 7f
        strokeCap = Paint.Cap.ROUND
        strokeJoin = Paint.Join.ROUND
        color = Color.parseColor("#FFFFFF")
    }

    // Translucent fill for enclosed lasso region
    private val fillPaint = Paint(Paint.ANTI_ALIAS_FLAG).apply {
        style = Paint.Style.FILL
        color = Color.parseColor("#258B5CF6")
    }

    // Flash animation paint for selected crop
    private val flashPaint = Paint(Paint.ANTI_ALIAS_FLAG).apply {
        style = Paint.Style.FILL
        color = Color.parseColor("#FFFFFF")
    }

    private val flashStrokePaint = Paint(Paint.ANTI_ALIAS_FLAG).apply {
        style = Paint.Style.STROKE
        strokeWidth = 8f
        strokeCap = Paint.Cap.ROUND
        strokeJoin = Paint.Join.ROUND
        color = Color.parseColor("#A78BFA")
    }

    // Screen edge aura (Google Circle to Search signature iridescent border)
    private val edgeAuraPaint = Paint(Paint.ANTI_ALIAS_FLAG).apply {
        style = Paint.Style.STROKE
        strokeWidth = 14f
    }

    private var flashRect = RectF()

    init {
        setLayerType(LAYER_TYPE_HARDWARE, null)
    }

    override fun onAttachedToWindow() {
        super.onAttachedToWindow()
        auraAnimator.start()
    }

    override fun onDetachedFromWindow() {
        super.onDetachedFromWindow()
        auraAnimator.cancel()
    }

    fun reset() {
        drawPath.reset()
        points.clear()
        isDrawing = false
        isFlashActive = false
        flashAlpha = 0f
        invalidate()
    }

    override fun onDraw(canvas: Canvas) {
        super.onDraw(canvas)

        val w = width.toFloat()
        val h = height.toFloat()
        if (w <= 0 || h <= 0) return

        // 1. Draw frozen screenshot
        screenshotBitmap?.let { bmp ->
            val src = Rect(0, 0, bmp.width, bmp.height)
            val dst = Rect(0, 0, width, height)
            canvas.drawBitmap(bmp, src, dst, null)
        }

        // 2. Dim background scrim
        canvas.drawRect(0f, 0f, w, h, scrimPaint)

        // 3. Google Circle to Search style iridescent edge aura
        val cx = w / 2f
        val cy = h / 2f
        val sweepGradient = SweepGradient(
            cx, cy,
            intArrayOf(
                Color.parseColor("#38BDF8"),
                Color.parseColor("#A855F7"),
                Color.parseColor("#EC4899"),
                Color.parseColor("#6366F1"),
                Color.parseColor("#38BDF8")
            ),
            null
        )
        val matrix = Matrix()
        matrix.setRotate(auraAngle, cx, cy)
        sweepGradient.setLocalMatrix(matrix)
        edgeAuraPaint.shader = sweepGradient
        canvas.drawRoundRect(7f, 7f, w - 7f, h - 7f, 32f, 32f, edgeAuraPaint)

        // 4. Draw user's lasso path
        if (!drawPath.isEmpty) {
            canvas.drawPath(drawPath, fillPaint)
            canvas.drawPath(drawPath, glowPaint)
            canvas.drawPath(drawPath, corePaint)
        }

        // 5. Draw selection pulse flash
        if (isFlashActive && flashAlpha > 0f) {
            flashPaint.alpha = (flashAlpha * 90).toInt().coerceIn(0, 255)
            flashStrokePaint.alpha = (flashAlpha * 255).toInt().coerceIn(0, 255)
            canvas.drawRoundRect(flashRect, 24f, 24f, flashPaint)
            canvas.drawRoundRect(flashRect, 24f, 24f, flashStrokePaint)
        }
    }

    @SuppressLint("ClickableViewAccessibility")
    override fun onTouchEvent(event: MotionEvent): Boolean {
        if (isFlashActive) return true

        val x = event.x
        val y = event.y

        when (event.actionMasked) {
            MotionEvent.ACTION_DOWN -> {
                reset()
                drawPath.moveTo(x, y)
                lastX = x
                lastY = y
                points.add(PointF(x, y))
                isDrawing = true
                invalidate()
                return true
            }

            MotionEvent.ACTION_MOVE -> {
                if (isDrawing) {
                    val dx = Math.abs(x - lastX)
                    val dy = Math.abs(y - lastY)
                    if (dx >= 4 || dy >= 4) {
                        drawPath.quadTo(lastX, lastY, (x + lastX) / 2f, (y + lastY) / 2f)
                        lastX = x
                        lastY = y
                        points.add(PointF(x, y))
                        invalidate()
                    }
                }
                return true
            }

            MotionEvent.ACTION_UP, MotionEvent.ACTION_CANCEL -> {
                if (isDrawing && points.isNotEmpty()) {
                    drawPath.lineTo(x, y)
                    points.add(PointF(x, y))
                    isDrawing = false

                    // Calculate bounding box
                    val bounds = RectF()
                    drawPath.computeBounds(bounds, true)

                    val density = resources.displayMetrics.density
                    val minSize = 90f * density

                    // If user tapped or circled a very small dot, create a comfortable bounding box
                    if (bounds.width() < minSize && bounds.height() < minSize) {
                        val centerX = if (bounds.width() > 0) bounds.centerX() else lastX
                        val centerY = if (bounds.height() > 0) bounds.centerY() else lastY
                        bounds.set(
                            centerX - minSize / 2f,
                            centerY - minSize / 2f,
                            centerX + minSize / 2f,
                            centerY + minSize / 2f
                        )
                    } else {
                        // Add comfortable 16dp padding
                        val pad = 16f * density
                        bounds.inset(-pad, -pad)
                    }

                    // Clamp to view
                    bounds.left = bounds.left.coerceIn(0f, width.toFloat())
                    bounds.top = bounds.top.coerceIn(0f, height.toFloat())
                    bounds.right = bounds.right.coerceIn(bounds.left + 20f, width.toFloat())
                    bounds.bottom = bounds.bottom.coerceIn(bounds.top + 20f, height.toFloat())

                    flashRect.set(bounds)
                    startFlashAndComplete(bounds)
                }
                return true
            }
        }
        return super.onTouchEvent(event)
    }

    private fun startFlashAndComplete(bounds: RectF) {
        val bmp = screenshotBitmap ?: return
        isFlashActive = true

        val animator = ValueAnimator.ofFloat(0f, 1f, 0.4f, 0f)
        animator.duration = 240
        animator.interpolator = DecelerateInterpolator()
        animator.addUpdateListener {
            flashAlpha = it.animatedValue as Float
            invalidate()
        }
        animator.addListener(object : android.animation.AnimatorListenerAdapter() {
            override fun onAnimationEnd(animation: android.animation.Animator) {
                isFlashActive = false
                cropAndEmit(bmp, bounds)
            }
        })
        animator.start()
    }

    private fun cropAndEmit(bmp: Bitmap, bounds: RectF) {
        try {
            // Map coordinates from view space to bitmap space
            val scaleX = bmp.width.toFloat() / width.toFloat()
            val scaleY = bmp.height.toFloat() / height.toFloat()

            val cropX = (bounds.left * scaleX).toInt().coerceIn(0, bmp.width - 1)
            val cropY = (bounds.top * scaleY).toInt().coerceIn(0, bmp.height - 1)
            val cropW = (bounds.width() * scaleX).toInt().coerceIn(1, bmp.width - cropX)
            val cropH = (bounds.height() * scaleY).toInt().coerceIn(1, bmp.height - cropY)

            val cropped = Bitmap.createBitmap(bmp, cropX, cropY, cropW, cropH)
            onSelectionComplete?.invoke(cropped)
        } catch (_: Exception) {
            onDismiss?.invoke()
        }
    }
}
